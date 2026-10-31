using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Imports;

/// <summary>What is wrong (or worth a note) in a workbook or one of its rows. Persian wording is the screen\'s job.</summary>
public enum ImportIssue
{
    // file level
    FileNotXlsx = 1,
    FileTooLarge,
    FileCorrupt,
    NoWorksheet,
    TooManyRows,
    HeaderRowMissing,
    HeaderMissing,
    HeaderDuplicate,
    HeaderUnknown,
    MoreThanOneWorksheet,
    NoDataRows,

    // row level
    ItemNoMissing,
    ItemNoNumeric,

    /// <summary>The same customer item reference occurs twice in the file (Decision Log #121 Q2).</summary>
    ItemNoDuplicate,

    /// <summary>The customer item reference is longer than 40 characters (a note, not an error: the row cannot be matched or created).</summary>
    CustomerRefTooLong,

    /// <summary>The row has no customer reference: it is always created as a new item and is never matched to an existing one (a note; a repeated import adds it again).</summary>
    NoCustomerRef,

    /// <summary>The assembly cell holds something other than "بله" or an empty cell.</summary>
    AssemblyFlagInvalid,

    /// <summary>The parent reference names no item of the request and no earlier row of the file.</summary>
    ParentRefUnknown,

    /// <summary>The parent is not an assembly (mark it "بله" in its own row, or in the request before importing).</summary>
    ParentNotAssembly,

    /// <summary>The assemblies would be nested more than four levels deep.</summary>
    ParentTooDeep,

    /// <summary>The parent already has stages defined, so children can no longer be attached (Decision Log #121 Q6).</summary>
    ParentLocked,

    /// <summary>The row names itself as its own parent.</summary>
    ParentIsSelf,
    PartNameMissing,
    QuantityInvalid,
    WorkTypeUnknown,
    WorkTypeMismatch,
    QuantityTypeMissing,
    QuantityTypeUnknown,
    MaterialMissing,
    MaterialUnknown,
    WeightMissing,
    WeightInvalid,
    DimensionInvalid,

    /// <summary>A REPAIR row lacks the delivery date or the warehouse item code (Decision Log #124 B5).</summary>
    RepairFieldsMissing,

    /// <summary>The delivery date is not a valid Jalali date.</summary>
    DeliveryDateInvalid,

    /// <summary>The delivery date is before the request date.</summary>
    DeliveryBeforeRequest,

    /// <summary>The warehouse item code is longer than 40 characters or has surrounding white space.</summary>
    WarehouseCodeInvalid,
    PriorityUnknown,
    StageIncomplete,
    StageGap,
    StageMinutesInvalid,
    StageUnitUnknown,
    StageMachineUnknown,
    StageMachineUnitMismatch,
    StagesIgnoredForExistingItem,
    StageHeaderBeyondLimit,
}

/// <summary>A message about the file (<see cref="SheetRow"/> null) or a row, with an optional detail (a header name, a stage number, a code, a first row number).</summary>
public sealed record ImportMessage(int? SheetRow, ImportIssue Issue, string? Detail, bool IsError);

public sealed record ImportStagePlan(int StageNo, string Title, decimal MinutesPerPiece, long ProductionUnitId, long? MachineId);

/// <summary>A valid, fully resolved worksheet row: the item\'s definition and its planned stages.</summary>
public sealed record ImportRowData(
    int SheetRowNo, string? ItemNo, string PartName, decimal Quantity, Priority Priority, ItemSpecification Specification, IReadOnlyList<ImportStagePlan> Stages, string? ParentRef = null, bool IsAssembly = false);

/// <summary>One data row after parsing: valid when <see cref="Data"/> is set; otherwise <see cref="Messages"/> say why.</summary>
public sealed record ParsedImportRow(int SheetRowNo, string? ItemNo, ImportRowData? Data, IReadOnlyList<ImportMessage> Messages);

public sealed record ParsedImport(IReadOnlyList<ImportMessage> FileMessages, IReadOnlyList<ParsedImportRow> Rows)
{
    public bool FileOk => FileMessages.All(m => !m.IsError);
}

/// <summary>The codes a worksheet may use, resolved to the ids of ACTIVE master records. Unknown codes are errors; nothing is created automatically.</summary>
public sealed record ImportLookups(
    IReadOnlyDictionary<string, long> QuantityTypes,
    IReadOnlyDictionary<string, long> Materials,
    IReadOnlyDictionary<string, long> Units,
    IReadOnlyDictionary<string, (long Id, long UnitId)> Machines);

/// <summary>
/// Parses the rows of the first worksheet into item definitions (Decision Log #16, #97). Pure: no database, no clock. Headers are matched by name (see <see cref="ItemImportTemplate"/>), every
/// row is validated on its own and gets all its errors at once, with the worksheet row number. Item numbers must be TEXT cells (a numeric cell would turn "1.10" into 1.1) and unique in the file.
/// </summary>
public static class ItemImportParser
{
    public static ParsedImport Parse(WorkbookReadResult workbook, WorkType workType, ImportLookups lookups, DateOnly? requestDate = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(lookups);
        var file = new List<ImportMessage>();
        if (workbook.Error != WorkbookError.None)
        {
            file.Add(new ImportMessage(null, workbook.Error switch
            {
                WorkbookError.NotAnXlsxFile => ImportIssue.FileNotXlsx,
                WorkbookError.TooLarge => ImportIssue.FileTooLarge,
                WorkbookError.NoWorksheet => ImportIssue.NoWorksheet,
                WorkbookError.TooManyRows => ImportIssue.TooManyRows,
                _ => ImportIssue.FileCorrupt,
            }, null, true));
            return new ParsedImport(file, []);
        }

        if (workbook.SheetCount > 1)
        {
            file.Add(new ImportMessage(null, ImportIssue.MoreThanOneWorksheet, null, false));
        }

        var headerRow = workbook.Rows.FirstOrDefault();
        if (headerRow is null)
        {
            file.Add(new ImportMessage(null, ImportIssue.HeaderRowMissing, null, true));
            return new ParsedImport(file, []);
        }

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var known = ItemImportTemplate.AcceptedHeaders.Select(ItemImportTemplate.Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var (index, cell) in headerRow.Cells.OrderBy(c => c.Key))
        {
            var name = ItemImportTemplate.Normalize(cell.Text);
            if (name.Length == 0)
            {
                continue;
            }

            if (!known.Contains(name))
            {
                // A stage column above the limit would silently drop part of a route, so it is an error rather than an ignored column.
                var beyond = ItemImportTemplate.IsStageHeaderBeyondLimit(name);
                file.Add(new ImportMessage(null, beyond ? ImportIssue.StageHeaderBeyondLimit : ImportIssue.HeaderUnknown, cell.Text.Trim(), beyond));
            }
            else if (!columns.TryAdd(name, index))
            {
                file.Add(new ImportMessage(null, ImportIssue.HeaderDuplicate, cell.Text.Trim(), true));
            }
        }

        if (columns.ContainsKey(ItemImportTemplate.Normalize(ItemImportTemplate.CustomerItemRef)) && columns.ContainsKey(ItemImportTemplate.Normalize(ItemImportTemplate.ItemNo)))
        {
            file.Add(new ImportMessage(null, ImportIssue.HeaderDuplicate, ItemImportTemplate.ItemNo, true));
        }

        foreach (var required in ItemImportTemplate.RequiredHeaders)
        {
            if (!columns.ContainsKey(ItemImportTemplate.Normalize(required)))
            {
                file.Add(new ImportMessage(null, ImportIssue.HeaderMissing, required, true));
            }
        }

        var dataRows = workbook.Rows.Skip(1).ToList();
        if (dataRows.Count == 0)
        {
            file.Add(new ImportMessage(null, ImportIssue.NoDataRows, null, true));
        }

        if (file.Any(m => m.IsError))
        {
            return new ParsedImport(file, []);
        }

        var firstRowOfItemNo = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<ParsedImportRow>(dataRows.Count);
        foreach (var row in dataRows)
        {
            rows.Add(ParseRow(row, columns, workType, lookups, firstRowOfItemNo, requestDate));
        }

        return new ParsedImport(file, rows);
    }

    private static ParsedImportRow ParseRow(WorkbookRow row, Dictionary<string, int> columns, WorkType workType, ImportLookups lookups, Dictionary<string, int> firstRowOfItemNo, DateOnly? requestDate)
    {
        var messages = new List<ImportMessage>();

        void Error(ImportIssue issue, string? detail = null) => messages.Add(new ImportMessage(row.RowNumber, issue, detail, true));

        string? Text(string header) =>
            columns.TryGetValue(ItemImportTemplate.Normalize(header), out var index) && row.Cells.TryGetValue(index, out var cell) && !string.IsNullOrWhiteSpace(cell.Text) ? cell.Text.Trim() : null;

        bool IsNumeric(string header) => columns.TryGetValue(ItemImportTemplate.Normalize(header), out var index) && row.Cells.TryGetValue(index, out var cell) && cell.IsNumber;

        // The customer's own item number (Decision Log #121 Q2): optional; when present it is exact text (a numeric cell would turn "1.10" into 1.1), at most 40 characters and unique in the file.
        // The older header "شماره آیتم" means the same column. A row without one is always a new item.
        var refHeader = columns.ContainsKey(ItemImportTemplate.Normalize(ItemImportTemplate.CustomerItemRef)) ? ItemImportTemplate.CustomerItemRef : ItemImportTemplate.ItemNo;
        var itemNo = Text(refHeader);
        if (itemNo is null)
        {
            messages.Add(new ImportMessage(row.RowNumber, ImportIssue.NoCustomerRef, null, false));
        }
        else if (IsNumeric(refHeader))
        {
            Error(ImportIssue.ItemNoNumeric, itemNo);
        }
        else if (itemNo.Length > RequestItem.MaxCustomerItemRefLength)
        {
            Error(ImportIssue.CustomerRefTooLong, itemNo);
        }
        else if (!firstRowOfItemNo.TryAdd(itemNo, row.RowNumber))
        {
            Error(ImportIssue.ItemNoDuplicate, firstRowOfItemNo[itemNo].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var parentRef = Text(ItemImportTemplate.ParentRef);
        if (parentRef is not null && itemNo is not null && string.Equals(parentRef, itemNo, StringComparison.Ordinal))
        {
            Error(ImportIssue.ParentIsSelf, parentRef);
        }

        var isAssembly = false;
        if (Text(ItemImportTemplate.Assembly) is { } assemblyText)
        {
            if (ItemImportTemplate.Normalize(assemblyText) == ItemImportTemplate.Normalize("بله"))
            {
                isAssembly = true;
            }
            else
            {
                Error(ImportIssue.AssemblyFlagInvalid, assemblyText);
            }
        }

        var partName = Text(ItemImportTemplate.PartName);
        if (partName is null)
        {
            Error(ImportIssue.PartNameMissing);
        }

        decimal quantity = 0;
        if (Text(ItemImportTemplate.Quantity) is not { } quantityText || !QuantityText.TryParse(quantityText, out quantity) || quantity <= 0)
        {
            Error(ImportIssue.QuantityInvalid, Text(ItemImportTemplate.Quantity));
        }

        // optional explicit work type; it must match the request\'s one type (Decision Log #84)
        if (Text(ItemImportTemplate.WorkType) is { } typeText)
        {
            var normalized = ItemImportTemplate.Normalize(typeText);
            if (normalized == ItemImportTemplate.Normalize("ساخت"))
            {
                if (workType != WorkType.Make)
                {
                    Error(ImportIssue.WorkTypeMismatch, typeText);
                }
            }
            else if (normalized == ItemImportTemplate.Normalize("تعمیر"))
            {
                if (workType != WorkType.Repair)
                {
                    Error(ImportIssue.WorkTypeMismatch, typeText);
                }
            }
            else
            {
                Error(ImportIssue.WorkTypeUnknown, typeText);
            }
        }

        // specification
        long? quantityTypeId = null;
        if (Text(ItemImportTemplate.QuantityType) is { } typeCode)
        {
            if (lookups.QuantityTypes.TryGetValue(typeCode, out var id))
            {
                quantityTypeId = id;
            }
            else
            {
                Error(ImportIssue.QuantityTypeUnknown, typeCode);
            }
        }
        else if (workType == WorkType.Make)
        {
            Error(ImportIssue.QuantityTypeMissing);
        }

        long? materialId = null;
        if (Text(ItemImportTemplate.Material) is { } materialCode)
        {
            if (lookups.Materials.TryGetValue(materialCode, out var id))
            {
                materialId = id;
            }
            else
            {
                Error(ImportIssue.MaterialUnknown, materialCode);
            }
        }
        else if (workType == WorkType.Make)
        {
            Error(ImportIssue.MaterialMissing);
        }

        decimal? weight = null;
        if (Text(ItemImportTemplate.Weight) is { } weightText)
        {
            if (QuantityText.TryParse(weightText, out var parsed))
            {
                weight = parsed;
            }
            else
            {
                Error(ImportIssue.WeightInvalid, weightText);
            }
        }
        else if (workType == WorkType.Make)
        {
            Error(ImportIssue.WeightMissing);
        }

        decimal? length = null;
        if (Text(ItemImportTemplate.Length) is { } lengthText)
        {
            if (QuantityText.TryParse(lengthText, out var parsed))
            {
                length = parsed;
            }
            else
            {
                Error(ImportIssue.DimensionInvalid, lengthText);
            }
        }

        decimal? diameter = null;
        if (Text(ItemImportTemplate.Diameter) is { } diameterText)
        {
            if (QuantityText.TryParse(diameterText, out var parsed))
            {
                diameter = parsed;
            }
            else
            {
                Error(ImportIssue.DimensionInvalid, diameterText);
            }
        }

        // REPAIR fields (Decision Log #124 B5): a Jalali delivery date on or after the request date, and the warehouse code.
        DateOnly? deliveryDate = null;
        if (Text(ItemImportTemplate.DeliveryDate) is { } deliveryText)
        {
            if (!ImportDates.TryParseDate(deliveryText, out var parsedDelivery))
            {
                Error(ImportIssue.DeliveryDateInvalid, deliveryText);
            }
            else if (requestDate is { } requested && parsedDelivery < requested)
            {
                Error(ImportIssue.DeliveryBeforeRequest, deliveryText);
            }
            else
            {
                deliveryDate = parsedDelivery;
            }
        }

        string? warehouseCode = null;
        if (Text(ItemImportTemplate.WarehouseCode) is { } warehouseText)
        {
            if (warehouseText.Length > ItemSpecification.MaxWarehouseItemCodeLength)
            {
                Error(ImportIssue.WarehouseCodeInvalid, warehouseText);
            }
            else
            {
                warehouseCode = warehouseText;
            }
        }

        if (workType == WorkType.Repair && messages.All(m => m.Issue is not (ImportIssue.DeliveryDateInvalid or ImportIssue.DeliveryBeforeRequest or ImportIssue.WarehouseCodeInvalid))
            && (deliveryDate is null || warehouseCode is null))
        {
            Error(ImportIssue.RepairFieldsMissing);
        }

        var specification = new ItemSpecification(quantityTypeId, materialId, weight, length, diameter, 1, deliveryDate, warehouseCode);
        if (messages.All(m => m.Issue is not (ImportIssue.WeightInvalid or ImportIssue.DimensionInvalid or ImportIssue.WeightMissing)))
        {
            switch (specification.Check(WorkType.Repair))
            {
                case SpecificationProblem.WeightInvalid:
                    Error(ImportIssue.WeightInvalid, Text(ItemImportTemplate.Weight));
                    break;
                case SpecificationProblem.DimensionInvalid:
                    Error(ImportIssue.DimensionInvalid, Text(ItemImportTemplate.Length) ?? Text(ItemImportTemplate.Diameter));
                    break;
            }
        }

        // priority (blank = Normal)
        var priority = Priority.Normal;
        if (Text(ItemImportTemplate.Priority) is { } priorityText)
        {
            var match = PriorityInfo.All.Cast<Priority?>().FirstOrDefault(p => ItemImportTemplate.Normalize(p!.Value.PersianName()) == ItemImportTemplate.Normalize(priorityText));
            if (match is { } found)
            {
                priority = found;
            }
            else
            {
                Error(ImportIssue.PriorityUnknown, priorityText);
            }
        }

        // stages: contiguous groups of title / minutes / unit code / machine code
        var stages = new List<ImportStagePlan>();
        var gap = false;
        for (var k = 1; k <= ItemImportTemplate.MaxStages; k++)
        {
            var title = Text(ItemImportTemplate.StageTitle(k));
            var minutesText = Text(ItemImportTemplate.StageMinutes(k));
            var unitCode = Text(ItemImportTemplate.StageUnit(k));
            var machineCode = Text(ItemImportTemplate.StageMachine(k));
            if (title is null && minutesText is null && unitCode is null && machineCode is null)
            {
                gap = true;
                continue;
            }

            if (gap)
            {
                Error(ImportIssue.StageGap, k.ToString(System.Globalization.CultureInfo.InvariantCulture));
                gap = false;
            }

            var number = k.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (title is null || title.Length > 200 || minutesText is null || unitCode is null)
            {
                Error(ImportIssue.StageIncomplete, number);
                continue;
            }

            if (!QuantityText.TryParse(minutesText, out var minutes) || minutes <= 0)
            {
                Error(ImportIssue.StageMinutesInvalid, number);
                continue;
            }

            if (!lookups.Units.TryGetValue(unitCode, out var unitId))
            {
                Error(ImportIssue.StageUnitUnknown, unitCode);
                continue;
            }

            long? machineId = null;
            if (machineCode is not null)
            {
                if (!lookups.Machines.TryGetValue(machineCode, out var machine))
                {
                    Error(ImportIssue.StageMachineUnknown, machineCode);
                    continue;
                }

                if (machine.UnitId != unitId)
                {
                    Error(ImportIssue.StageMachineUnitMismatch, machineCode);
                    continue;
                }

                machineId = machine.Id;
            }

            stages.Add(new ImportStagePlan(stages.Count + 1, title, minutes, unitId, machineId));
        }

        if (messages.Any(m => m.IsError))
        {
            return new ParsedImportRow(row.RowNumber, itemNo, null, messages);
        }

        return new ParsedImportRow(row.RowNumber, itemNo, new ImportRowData(row.RowNumber, itemNo, partName!, quantity, priority, specification, stages, parentRef, isAssembly), messages);
    }
}

/// <summary>Reads the Jalali dates of a worksheet. Pure conversion, so the display digit style and time zone do not matter.</summary>
internal static class ImportDates
{
    private static readonly ERP.MES.Domain.Calendar.JalaliDateService Dates = new(ERP.MES.Domain.Calendar.DigitStyle.Latin, TimeZoneInfo.Utc);

    public static bool TryParseDate(string text, out DateOnly gregorian)
    {
        gregorian = default;
        if (!Dates.TryParseDate(text, out var jalali, out _))
        {
            return false;
        }

        gregorian = Dates.ToGregorian(jalali);
        return true;
    }
}
