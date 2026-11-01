using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Imports;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Imports;

public enum ImportRowStatus
{
    /// <summary>A new item number: the item (and its stages) will be created.</summary>
    New = 1,

    /// <summary>The item number exists with identical values: nothing to do.</summary>
    Unchanged,

    /// <summary>The item number exists with different values and may still be revised: the user must choose Skip or Update.</summary>
    Conflict,

    /// <summary>The item number exists with different values but the item already has a route or a QC approval, so it cannot be revised by import: it can only be skipped.</summary>
    Locked,

    /// <summary>The row has errors; the import cannot be committed until the file is corrected.</summary>
    Invalid,
}

public enum ImportField
{
    PartName = 1,
    Quantity,
    Priority,
    QuantityType,
    Material,
    Weight,
    Length,
    Diameter,
    DeliveryDate,
    WarehouseItemCode,
}

/// <summary>One field that differs between the existing item (<see cref="Old"/>) and the worksheet (<see cref="New"/>), as invariant text (null = empty).</summary>
public sealed record ImportFieldChange(ImportField Field, string? Old, string? New);

public sealed record ImportRowPreview(
    int SheetRowNo, string? ItemNo, ImportRowStatus Status, IReadOnlyList<ImportFieldChange> Changes, IReadOnlyList<ImportMessage> Messages);

/// <summary>The result of reading and checking a file without writing anything. <see cref="RequestVersion"/> and <see cref="FileHash"/> are passed back to <see cref="ItemImportService.CommitAsync"/>.</summary>
public sealed record ImportPreview(
    string FileName,
    string FileHash,
    long RequestVersion,
    IReadOnlyList<ImportMessage> FileMessages,
    IReadOnlyList<ImportRowPreview> Rows)
{
    public int Detected => Rows.Count;

    public int NewCount => Rows.Count(r => r.Status == ImportRowStatus.New);

    public int UnchangedCount => Rows.Count(r => r.Status == ImportRowStatus.Unchanged);

    public int ConflictCount => Rows.Count(r => r.Status is ImportRowStatus.Conflict or ImportRowStatus.Locked);

    public int InvalidCount => Rows.Count(r => r.Status == ImportRowStatus.Invalid);

    public bool CanCommit => FileMessages.All(m => !m.IsError) && InvalidCount == 0 && Rows.Count > 0;
}

public enum ConflictChoice
{
    Skip = 1,
    Update,
}

/// <summary>The user\'s choice for one conflicting worksheet row.</summary>
public sealed record ImportDecision(int SheetRowNo, ConflictChoice Choice);

public sealed record ImportCommitResult(int Created, int Updated, int Unchanged, int Skipped, long? ImportId)
{
    public bool ChangedAnything => Created + Updated > 0;
}

/// <summary>
/// Excel import of items into a request (Decision Log #16, #97; docs/ITEM_IMPORT_TEMPLATE.md). Planning only, and only once the same gates as manual definition hold. <see cref="PreviewAsync"/> reads and
/// checks the whole file and writes nothing; <see cref="CommitAsync"/> re-reads the file (nothing the screen holds is trusted), refuses it when any row is invalid or any conflict has no explicit
/// choice, and then creates and revises items, writes the import log and the audit entries in ONE transaction. An existing item number with different values is a conflict: Skip or Update, never
/// a silent overwrite; an item that already has a route or a QC approval can only be skipped. Importing the same file again changes nothing.
/// </summary>
public sealed class ItemImportService(
    IRequestRepository requests,
    IItemImportRepository imports,
    IQcMasterRepository qc,
    IMaterialRepository materials,
    IPlanningMasterRepository planning,
    IWorkbookReader reader,
    RequestGateService gates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    /// <summary>Largest accepted file (20 MiB, like the PDF default).</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    private static readonly Permission[] ImportPermissions = [Permission.ImportItemsFromExcel, Permission.ImportTechnicalItems];

    /// <summary>
    /// Who is importing (Decision Log #125 Q15): Planning imports everything once the definition gates hold; Drawing imports the technical columns only, while the request is under Drawing review.
    /// Null when the user may do neither.
    /// </summary>
    private async Task<(Principal User, bool TechnicalOnly)?> ResolveScopeAsync(Principal? principal, CancellationToken cancellationToken)
    {
        var set = await authorization.EvaluateAsync(principal, ImportPermissions, cancellationToken);
        if (set.Current is null)
        {
            return null;
        }

        return set.Has(Permission.ImportItemsFromExcel) ? (set.Current, false) : set.Has(Permission.ImportTechnicalItems) ? (set.Current, true) : null;
    }

    public async Task<bool> CanImportAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        await ResolveScopeAsync(principal, cancellationToken) is not null;

    public async Task<Outcome<ImportPreview>> PreviewAsync(Principal? principal, long requestId, string? fileName, byte[]? content, CancellationToken cancellationToken = default)
    {
        if (await ResolveScopeAsync(principal, cancellationToken) is not { } scope)
        {
            return Outcome<ImportPreview>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var loaded = await LoadAsync(requestId, fileName, content, scope.TechnicalOnly, cancellationToken);
            return loaded.Failure is { } failure ? failure.As<ImportPreview>() : Outcome<ImportPreview>.Ok(loaded.Preview!);
        });
    }

    public async Task<Outcome<ImportCommitResult>> CommitAsync(
        Principal? principal,
        long requestId,
        long expectedRequestVersion,
        string? fileName,
        byte[]? content,
        string? expectedFileHash,
        IReadOnlyList<ImportDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (await ResolveScopeAsync(principal, cancellationToken) is not { } scope)
        {
            return Outcome<ImportCommitResult>.Fail(OperationStatus.NotAuthorized);
        }

        var user = scope.User;
        var technicalOnly = scope.TechnicalOnly;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var loaded = await LoadAsync(requestId, fileName, content, technicalOnly, cancellationToken);
                if (loaded.Failure is { } failure)
                {
                    return failure.As<ImportCommitResult>();
                }

                var preview = loaded.Preview!;
                if (!string.Equals(preview.FileHash, expectedFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Outcome<ImportCommitResult>.Invalid(ValidationIssue.ImportFileChanged);
                }

                if (!preview.CanCommit)
                {
                    return Outcome<ImportCommitResult>.Invalid(ValidationIssue.ImportHasErrors);
                }

                var choices = decisions.GroupBy(d => d.SheetRowNo).ToDictionary(g => g.Key, g => g.Last().Choice);
                foreach (var row in preview.Rows.Where(r => r.Status is ImportRowStatus.Conflict or ImportRowStatus.Locked))
                {
                    if (!choices.TryGetValue(row.SheetRowNo, out var choice))
                    {
                        return Outcome<ImportCommitResult>.Invalid(ValidationIssue.ImportDecisionMissing);
                    }

                    if (choice == ConflictChoice.Update && row.Status == ImportRowStatus.Locked)
                    {
                        return Outcome<ImportCommitResult>.Invalid(ValidationIssue.ImportDecisionInvalid);
                    }
                }

                var data = loaded.Parsed!.Rows.Where(r => r.Data is not null).ToDictionary(r => r.SheetRowNo, r => r.Data!);
                var perType = (await qc.ListQuantityTypesAsync(cancellationToken)).ToDictionary(t => t.Value.Id, t => t.Value.BaseUnitsPerType);

                // QC inspects in base units: the item keeps how many base units one ordered unit counts (Decision Log #101).
                ItemSpecification WithBaseUnits(ItemSpecification spec) => spec.QuantityTypeId is { } id && perType.TryGetValue(id, out var units) ? spec with { BaseUnitsPerUnit = units } : spec;
                var outcomes = new List<(ImportRowPreview Row, ItemImportOutcome Outcome)>();
                foreach (var row in preview.Rows)
                {
                    outcomes.Add((row, row.Status switch
                    {
                        ImportRowStatus.New => ItemImportOutcome.Created,
                        ImportRowStatus.Unchanged => ItemImportOutcome.Unchanged,
                        _ => choices[row.SheetRowNo] == ConflictChoice.Update ? ItemImportOutcome.Updated : ItemImportOutcome.Skipped,
                    }));
                }

                var created = outcomes.Count(o => o.Outcome == ItemImportOutcome.Created);
                var updated = outcomes.Count(o => o.Outcome == ItemImportOutcome.Updated);
                var unchanged = outcomes.Count(o => o.Outcome == ItemImportOutcome.Unchanged);
                var skipped = outcomes.Count(o => o.Outcome == ItemImportOutcome.Skipped);
                if (created + updated == 0)
                {
                    // Idempotence: nothing to write, nothing logged.
                    return Outcome<ImportCommitResult>.Ok(new ImportCommitResult(0, 0, unchanged, skipped, null));
                }

                var hash = preview.FileHash;
                var name = preview.FileName;
                var saved = await imports.CommitAsync(
                    requestId,
                    expectedRequestVersion,
                    request =>
                    {
                        var definedHere = new Dictionary<string, RequestItem>(StringComparer.Ordinal);
                        foreach (var (row, outcome) in outcomes)
                        {
                            var rowData = data.GetValueOrDefault(row.SheetRowNo);
                            if (outcome == ItemImportOutcome.Created)
                            {
                                var parent = rowData!.ParentRef is { } parentRef ? definedHere.GetValueOrDefault(parentRef) ?? request.FindByCustomerItemRef(parentRef) : null;
                                var item = request.DefineItem(0, rowData.ItemNo, rowData.PartName, rowData.Quantity, WithBaseUnits(rowData.Specification), user.UserId, now, rowData.Priority, parent, rowData.IsAssembly);
                                if (rowData.ItemNo is not null)
                                {
                                    definedHere[rowData.ItemNo] = item;
                                }

                                foreach (var stage in rowData.Stages)
                                {
                                    item.DefineInitialStage(stage.Title, stage.MinutesPerPiece, null, stage.ProductionUnitId, stage.MachineId);
                                }
                            }
                            else if (outcome == ItemImportOutcome.Updated)
                            {
                                // A Drawing import never changes Planning's priority (Decision Log #125 Q15).
                                var priority = technicalOnly ? request.FindByCustomerItemRef(rowData!.ItemNo)!.Priority : rowData!.Priority;
                                request.ReviseItem(rowData.ItemNo!, rowData.PartName, rowData.Quantity, priority, WithBaseUnits(rowData.Specification), now);
                            }
                        }
                    },
                    request => new ItemImport(requestId, name, hash, user.UserId, now, outcomes.Select(o => new ItemImportRow(o.Row.SheetRowNo, o.Row.ItemNo ?? string.Empty, o.Outcome))),
                    request =>
                    {
                        var entries = new List<AuditLogEntry>
                        {
                            Audit(user, now, AuditActions.ItemImportCommitted, requestId, $"Request {request.RequestNo.Value}: items imported from '{name}' (SHA-256 {hash}): {created} created, {updated} updated, {unchanged} unchanged, {skipped} skipped"),
                        };
                        entries.AddRange(outcomes.Where(o => o.Outcome == ItemImportOutcome.Created).Select(o =>
                            Audit(user, now, AuditActions.ItemDefined, requestId, $"Request {request.RequestNo.Value}: item '{o.Row.ItemNo}' created by import (row {o.Row.SheetRowNo})")));
                        entries.AddRange(outcomes.Where(o => o.Outcome == ItemImportOutcome.Updated).Select(o =>
                            Audit(user, now, AuditActions.ItemRevisedByImport, requestId, $"Request {request.RequestNo.Value}: item '{o.Row.ItemNo}' revised by import (row {o.Row.SheetRowNo}); fields: {string.Join(", ", o.Row.Changes.Select(c => c.Field))}")));
                        return entries;
                    },
                    cancellationToken);
                _ = saved;
                return Outcome<ImportCommitResult>.Ok(new ImportCommitResult(created, updated, unchanged, skipped, null));
            });
        }
        catch (ItemSpecificationException)
        {
            return Outcome<ImportCommitResult>.Invalid(ValidationIssue.ImportHasErrors);
        }
        catch (ItemHierarchyException exception)
        {
            // For example more than 999 items under one parent (Decision Log #121 Q3): the whole import is refused and nothing is saved.
            return Outcome<ImportCommitResult>.Invalid(ItemDefinitionService.HierarchyIssue(exception.Problem));
        }
        catch (InvalidOperationException)
        {
            // The request or an item is no longer in a state the import may change (for example somebody defined a route meanwhile).
            return Outcome<ImportCommitResult>.Fail(OperationStatus.InvalidState);
        }
    }

    private sealed record Loaded(ImportPreview? Preview, ParsedImport? Parsed, Failed? Failure);

    private sealed record Failed(OperationStatus Status, ValidationIssue Issue)
    {
        public Outcome<T> As<T>() => Status == OperationStatus.ValidationFailed ? Outcome<T>.Invalid(Issue) : Outcome<T>.Fail(Status);
    }

    private async Task<Loaded> LoadAsync(long requestId, string? fileName, byte[]? content, bool technicalOnly, CancellationToken cancellationToken)
    {
        var current = await requests.FindAsync(requestId, cancellationToken);
        if (current is null)
        {
            return new Loaded(null, null, new Failed(OperationStatus.NotFound, ValidationIssue.None));
        }

        if (content is null || content.Length == 0 || string.IsNullOrWhiteSpace(fileName))
        {
            return new Loaded(null, null, new Failed(OperationStatus.ValidationFailed, ValidationIssue.ImportFileMissing));
        }

        if (content.LongLength > MaxFileBytes)
        {
            return new Loaded(null, null, new Failed(OperationStatus.ValidationFailed, ValidationIssue.ImportFileTooLarge));
        }

        if (current.Value.Status is RequestStatus.Cancelled or RequestStatus.Closed)
        {
            return new Loaded(null, null, new Failed(OperationStatus.ValidationFailed, ValidationIssue.RequestClosedForItems));
        }

        if (technicalOnly)
        {
            // Drawing works before the Drawing result, the contract and the inquiries exist: only the review status matters (Decision Log #125 Q15). Stage definition and release keep all the gates.
            if (current.Value.Status != RequestStatus.UnderDrawingReview)
            {
                return new Loaded(null, null, new Failed(OperationStatus.ValidationFailed, ValidationIssue.TechnicalImportStatus));
            }
        }
        else
        {
            var gate = await gates.EvaluateAsync(requestId, cancellationToken);
            if (gate is null || !gate.DefinitionAllowed)
            {
                return new Loaded(null, null, new Failed(OperationStatus.ValidationFailed, ValidationIssue.ItemGateNotSatisfied));
            }
        }

        var lookups = await BuildLookupsAsync(cancellationToken);
        var parsed = ItemImportParser.Parse(reader.Read(content), current.Value.WorkType, lookups, current.Value.RequestDate, technicalOnly);
        var rows = parsed.Rows.Select(r => Classify(r, current.Value, parsed.Rows, technicalOnly)).ToList();
        var preview = new ImportPreview(fileName.Trim(), DocumentHash.Sha256Hex(content), current.Version, parsed.FileMessages, rows);
        return new Loaded(preview, parsed, null);
    }

    private async Task<ImportLookups> BuildLookupsAsync(CancellationToken cancellationToken)
    {
        var types = (await qc.ListQuantityTypesAsync(cancellationToken)).Where(t => t.Value.IsActive).ToDictionary(t => t.Value.Code, t => t.Value.Id, StringComparer.Ordinal);
        var mats = (await materials.ListMaterialsAsync(cancellationToken)).Where(m => m.Value.IsActive).ToDictionary(m => m.Value.Code, m => m.Value.Id, StringComparer.Ordinal);
        var units = (await planning.ListUnitsAsync(cancellationToken)).Where(u => u.Value.IsActive).ToDictionary(u => u.Value.Code, u => u.Value.Id, StringComparer.Ordinal);
        var machines = (await planning.ListMachinesAsync(cancellationToken)).Where(m => m.Value.IsActive).ToDictionary(m => m.Value.Code, m => (m.Value.Id, m.Value.ProductionUnitId), StringComparer.Ordinal);
        var titles = (await planning.ListStageTitlesAsync(cancellationToken)).Where(t => t.Value.IsActive).Select(t => t.Value.Title).ToHashSet(StringComparer.Ordinal);
        return new ImportLookups(types, mats, units, machines, titles);
    }

    private static ImportRowPreview Classify(ParsedImportRow row, Request request, IReadOnlyList<ParsedImportRow> fileRows, bool technicalOnly)
    {
        if (row.Data is not { } data)
        {
            return new ImportRowPreview(row.SheetRowNo, row.ItemNo, ImportRowStatus.Invalid, [], row.Messages);
        }

        // Existing items are matched ONLY by the customer reference (Decision Log #121 Q2): never by row number, description or the system number. A row without a reference is always new.
        var existing = request.FindByCustomerItemRef(data.ItemNo);
        if (existing is null)
        {
            var hierarchy = HierarchyProblem(data, request, fileRows);
            return hierarchy is { } error
                ? new ImportRowPreview(row.SheetRowNo, data.ItemNo, ImportRowStatus.Invalid, [], [.. row.Messages, error])
                : new ImportRowPreview(row.SheetRowNo, data.ItemNo, ImportRowStatus.New, [], row.Messages);
        }

        var changes = new List<ImportFieldChange>();

        void Compare<T>(ImportField field, T oldValue, T newValue)
        {
            var oldText = Invariant(oldValue);
            var newText = Invariant(newValue);
            if (oldText != newText)
            {
                changes.Add(new ImportFieldChange(field, oldText, newText));
            }
        }

        Compare(ImportField.PartName, existing.PartName, data.PartName);
        Compare(ImportField.Quantity, existing.RequestedQuantity, data.Quantity);
        if (!technicalOnly)
        {
            Compare(ImportField.Priority, existing.Priority, data.Priority);
        }

        Compare(ImportField.QuantityType, existing.QuantityTypeId, data.Specification.QuantityTypeId);
        Compare(ImportField.Material, existing.MaterialId, data.Specification.MaterialId);
        Compare(ImportField.Weight, existing.WeightKgPerBaseUnit, data.Specification.WeightKgPerBaseUnit);
        Compare(ImportField.Length, existing.LengthMm, data.Specification.LengthMm);
        Compare(ImportField.Diameter, existing.DiameterMm, data.Specification.DiameterMm);
        Compare(ImportField.DeliveryDate, existing.DeliveryDate, data.Specification.DeliveryDate);
        Compare(ImportField.WarehouseItemCode, existing.WarehouseItemCode, data.Specification.WarehouseItemCode);

        // The stage columns apply to NEW items only. For an existing item they are not compared as data; a note says its stages are kept.
        var messages = (data.Stages.Count > 0 ? new[] { new ImportMessage(row.SheetRowNo, ImportIssue.StagesIgnoredForExistingItem, data.ItemNo, false) } : []).Concat(row.Messages).ToArray();
        if (changes.Count == 0)
        {
            return new ImportRowPreview(row.SheetRowNo, data.ItemNo, ImportRowStatus.Unchanged, [], messages);
        }

        return new ImportRowPreview(row.SheetRowNo, data.ItemNo, existing.IsDefinitionRevisable ? ImportRowStatus.Conflict : ImportRowStatus.Locked, changes, messages);
    }

    /// <summary>
    /// The hierarchy of a NEW row (Decision Log #121 Q4): the parent is an item already in the request or an earlier row of the file, it is an assembly, it has no stages yet and the nesting stays
    /// within four levels. Returns the problem as a row error, or null when the row is fine.
    /// </summary>
    private static ImportMessage? HierarchyProblem(ImportRowData data, Request request, IReadOnlyList<ParsedImportRow> fileRows)
    {
        if (data.ParentRef is not { } parentRef)
        {
            return null;
        }

        ImportMessage Error(ImportIssue issue) => new(data.SheetRowNo, issue, parentRef, true);

        var chain = 1;
        var current = parentRef;
        var assembly = false;
        var locked = false;
        var first = true;
        var guard = 0;
        while (current is not null && guard++ < Request.MaxHierarchyDepth + 2)
        {
            var earlier = fileRows.FirstOrDefault(r => r.Data is { } d && d.SheetRowNo < data.SheetRowNo && string.Equals(d.ItemNo, current, StringComparison.Ordinal))?.Data;
            var stored = request.FindByCustomerItemRef(current);
            if (earlier is null && stored is null)
            {
                return Error(first ? ImportIssue.ParentRefUnknown : ImportIssue.ParentRefUnknown);
            }

            chain++;
            if (first)
            {
                assembly = earlier?.IsAssembly ?? stored!.IsAssembly;
                locked = earlier is null && stored!.HasStages;
                first = false;
            }

            current = earlier is not null ? earlier.ParentRef : stored!.Parent?.CustomerItemRef;
        }

        if (!assembly)
        {
            return Error(ImportIssue.ParentNotAssembly);
        }

        if (locked)
        {
            return Error(ImportIssue.ParentLocked);
        }

        return chain > Request.MaxHierarchyDepth ? Error(ImportIssue.ParentTooDeep) : null;
    }

    private static string? Invariant<T>(T value) => value switch
    {
        null => null,
        decimal d => d.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private AuditLogEntry Audit(Principal user, DateTime now, string action, long requestId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, "Request", requestId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary);
}
