using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Imports;

/// <summary>What the import did with one worksheet row.</summary>
public enum ItemImportOutcome
{
    Created = 1,
    Updated,
    Unchanged,

    /// <summary>A conflict (or a locked item) the user chose to skip.</summary>
    Skipped,
}

/// <summary>
/// The log of one committed item import (docs/WORKFLOW_REDESIGN.md section 12; Decision Log #16, #97): who imported which file (by hash) into which request, when, and what happened to every
/// row. Written in the same transaction as the items, append-only. An import that changed nothing is not logged.
/// </summary>
public sealed class ItemImport
{
    private readonly List<ItemImportRow> _rows = [];

    private ItemImport()
    {
        FileName = string.Empty;
        FileHash = string.Empty;
    }

    public ItemImport(long requestId, string fileName, string fileHash, long importedByUserId, DateTime importedAtUtc, IEnumerable<ItemImportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        RequestId = requestId;
        FileName = fileName;
        FileHash = fileHash;
        ImportedByUserId = importedByUserId;
        ImportedAtUtc = importedAtUtc;
        _rows.AddRange(rows);
    }

    public long Id { get; }

    public long RequestId { get; private set; }

    public string FileName { get; private set; }

    /// <summary>SHA-256 of the file content, upper-case hexadecimal.</summary>
    public string FileHash { get; private set; }

    public long ImportedByUserId { get; private set; }

    public DateTime ImportedAtUtc { get; private set; }

    public IReadOnlyList<ItemImportRow> Rows => _rows;
}

public sealed class ItemImportRow
{
    private ItemImportRow()
    {
        ItemNo = string.Empty;
    }

    public ItemImportRow(int sheetRowNo, string itemNo, ItemImportOutcome outcome)
    {
        SheetRowNo = sheetRowNo;
        ItemNo = itemNo;
        Outcome = outcome;
    }

    public long Id { get; }

    /// <summary>The row number in the worksheet (1-based, as Excel shows it).</summary>
    public int SheetRowNo { get; private set; }

    public string ItemNo { get; private set; }

    public ItemImportOutcome Outcome { get; private set; }
}
