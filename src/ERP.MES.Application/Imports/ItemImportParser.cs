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
    ItemNoDuplicate,
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
    PriorityUnknown,
    StageIncomplete,
    StageGap,
    StageMinutesInvalid,
    StageUnitUnknown,
    StageMachineUnknown,
    StageMachineUnitMismatch,
    StagesIgnoredForExistingItem,
}

/// <summary>A message about the file (<see cref="SheetRow"/> null) or a row, with an optional detail (a header name, a stage number, a code, a first row number).</summary>
public sealed record ImportMessage(int? SheetRow, ImportIssue Issue, string? Detail, bool IsError);

public sealed record ImportStagePlan(int StageNo, string Title, decimal MinutesPerPiece, long ProductionUnitId, long? MachineId);

/// <summary>A valid, fully resolved worksheet row: the item\'s definition and its planned stages.</summary>
public sealed record ImportRowData(int SheetRowNo, string ItemNo, string PartName, decimal Quantity, Priority Priority, ItemSpecification Specification, IReadOnlyList<ImportStagePlan> Stages);

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
    public static ParsedImport Parse(WorkbookReadResult workbook, WorkType workType, ImportLookups lookups)
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
        var known = ItemImportTemplate.Headers.Select(ItemImportTemplate.Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var (index, cell) in headerRow.Cells.OrderBy(c => c.Key))
        {
            var name = ItemImportTemplate.Normalize(cell.Text);
            if (name.Length == 0)
            {
                continue;
            }

            if (!known.Contains(name))
            {
                file.Add(new ImportMessage(null, ImportIssue.HeaderUnknown, cell.Text.Trim(), false));
            }
            else if (!columns.TryAdd(name, index))
            {
                file.Add(new ImportMessage(null, ImportIssue.HeaderDuplicate, cell.Text.Trim(), true));
            }
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
            rows.Add(ParseRow(row, columns, workType, lookups, firstRowOfItemNo));
        }

        return new ParsedImport(file, rows);
    }

    private static ParsedImportRow ParseRow(WorkbookRow row, Dictionary<string, int> columns, WorkType workType, ImportLookups lookups, Dictionary<string, int> firstRowOfItemNo)
    {
        var messages = new List<ImportMessage>();

        void Error(ImportIssue issue, string? detail = null) => messages.Add(new ImportMessage(row.RowNumber, issue, detail, true));

        string? Text(string header) =>
            columns.TryGetValue(ItemImportTemplate.Normalize(header), out var index) && row.Cells.TryGetValue(index, out var cell) && !string.IsNullOrWhiteSpace(cell.Text) ? cell.Text.Trim() : null;

        bool IsNumeric(string header) => columns.TryGetValue(ItemImportTemplate.Normalize(header), out var index) && row.Cells.TryGetValue(index, out var cell) && cell.IsNumber;

        // item number: text, present, unique in the file
        var itemNo = Text(ItemImportTemplate.ItemNo);
        if (itemNo is null)
        {
            Error(ImportIssue.ItemNoMissing);
        }
        else if (IsNumeric(ItemImportTemplate.ItemNo))
        {
            Error(ImportIssue.ItemNoNumeric, itemNo);
        }
        else if (!firstRowOfItemNo.TryAdd(itemNo, row.RowNumber))
        {
            Error(ImportIssue.ItemNoDuplicate, firstRowOfItemNo[itemNo].ToString(System.Globalization.CultureInfo.InvariantCulture));
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

        var specification = new ItemSpecification(quantityTypeId, materialId, weight, length, diameter);
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

        if (messages.Count > 0)
        {
            return new ParsedImportRow(row.RowNumber, itemNo, null, messages);
        }

        return new ParsedImportRow(row.RowNumber, itemNo, new ImportRowData(row.RowNumber, itemNo!, partName!, quantity, priority, specification, stages), []);
    }
}
