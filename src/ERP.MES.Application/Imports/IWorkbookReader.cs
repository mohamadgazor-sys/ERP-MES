namespace ERP.MES.Application.Imports;

/// <summary>One worksheet cell as text. <see cref="IsNumber"/> is true for a numeric cell (so an item number typed as a number, which would lose "1.10" to "1.1", can be refused).</summary>
public sealed record WorkbookCell(string Text, bool IsNumber);

/// <summary>One worksheet row: its row number as Excel shows it (1-based) and its non-empty cells by column number (1-based).</summary>
public sealed record WorkbookRow(int RowNumber, IReadOnlyDictionary<int, WorkbookCell> Cells);

public enum WorkbookError
{
    None = 0,

    /// <summary>Not an .xlsx workbook (for example an old .xls file, a CSV or another file).</summary>
    NotAnXlsxFile,

    /// <summary>The file or its expanded content is larger than the limit.</summary>
    TooLarge,

    /// <summary>The workbook is damaged or uses a layout that cannot be read.</summary>
    Corrupt,

    NoWorksheet,

    TooManyRows,
}

/// <summary>The rows of the FIRST worksheet and how many worksheets the workbook has.</summary>
public sealed record WorkbookReadResult(WorkbookError Error, IReadOnlyList<WorkbookRow> Rows, int SheetCount)
{
    public static WorkbookReadResult Failed(WorkbookError error) => new(error, [], 0);
}

/// <summary>Reads an .xlsx workbook. Implemented in Infrastructure; no spreadsheet package is used (Decision Log #97).</summary>
public interface IWorkbookReader
{
    WorkbookReadResult Read(byte[] content);
}

/// <summary>Writes a one-sheet .xlsx workbook with a header row and example rows, for the canonical import template.</summary>
public interface IWorkbookWriter
{
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);
}
