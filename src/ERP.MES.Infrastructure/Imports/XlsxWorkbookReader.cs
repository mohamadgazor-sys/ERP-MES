using System.IO.Compression;
using System.Xml;
using ERP.MES.Application.Imports;

namespace ERP.MES.Infrastructure.Imports;

/// <summary>
/// Reads the first worksheet of an .xlsx workbook with the .NET base libraries only (zip + XML); no spreadsheet package is used (Decision Log #97). It is defensive: the file is untrusted, so DTD
/// processing is prohibited, the expanded size and the entry count are capped (counted on what is actually read, not on what the zip header claims), and anything it cannot understand is
/// reported as an error rather than guessed. Shared strings, inline strings, numbers and booleans are read; formulas are read as their cached value; merged cells and styles are not interpreted.
/// </summary>
public sealed class XlsxWorkbookReader : IWorkbookReader
{
    /// <summary>Most worksheet rows read (the requirement is "about 1,000 items").</summary>
    public const int MaxRows = 5001;

    private const long MaxExpandedBytes = 100L * 1024 * 1024;
    private const int MaxEntries = 500;

    public WorkbookReadResult Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length < 4 || content[0] != (byte)'P' || content[1] != (byte)'K')
        {
            return WorkbookReadResult.Failed(WorkbookError.NotAnXlsxFile);
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries)
            {
                return WorkbookReadResult.Failed(WorkbookError.TooLarge);
            }

            var budget = new Budget(MaxExpandedBytes);
            var workbook = Find(zip, "xl/workbook.xml");
            if (workbook is null)
            {
                return WorkbookReadResult.Failed(WorkbookError.NotAnXlsxFile);
            }

            var sheets = ReadSheets(workbook, budget);
            if (sheets.Count == 0)
            {
                return WorkbookReadResult.Failed(WorkbookError.NoWorksheet);
            }

            var relationships = Find(zip, "xl/_rels/workbook.xml.rels") is { } rels ? ReadRelationships(rels, budget) : [];
            var first = sheets.FirstOrDefault(s => !s.Hidden) ?? sheets[0];
            if (!relationships.TryGetValue(first.RelationshipId, out var target))
            {
                return WorkbookReadResult.Failed(WorkbookError.Corrupt);
            }

            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            var sheetEntry = Find(zip, path);
            if (sheetEntry is null)
            {
                return WorkbookReadResult.Failed(WorkbookError.Corrupt);
            }

            var shared = Find(zip, "xl/sharedStrings.xml") is { } sst ? ReadSharedStrings(sst, budget) : [];
            var rows = ReadRows(sheetEntry, shared, budget, out var tooMany);
            return tooMany ? WorkbookReadResult.Failed(WorkbookError.TooManyRows) : new WorkbookReadResult(WorkbookError.None, rows, sheets.Count);
        }
        catch (InvalidDataException)
        {
            return WorkbookReadResult.Failed(WorkbookError.NotAnXlsxFile);
        }
        catch (ExpandedSizeException)
        {
            return WorkbookReadResult.Failed(WorkbookError.TooLarge);
        }
        catch (XmlException)
        {
            return WorkbookReadResult.Failed(WorkbookError.Corrupt);
        }
        catch (FormatException)
        {
            return WorkbookReadResult.Failed(WorkbookError.Corrupt);
        }
    }

    private sealed record SheetRef(string Name, string RelationshipId, bool Hidden);

    private sealed class ExpandedSizeException : Exception;

    private sealed class Budget(long limit)
    {
        private long _remaining = limit;

        public void Spend(int bytes)
        {
            _remaining -= bytes;
            if (_remaining < 0)
            {
                throw new ExpandedSizeException();
            }
        }
    }

    /// <summary>Counts the bytes actually decompressed against the shared budget.</summary>
    private sealed class CountingStream(Stream inner, Budget budget) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            budget.Spend(read);
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static ZipArchiveEntry? Find(ZipArchive zip, string path) =>
        zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    private static XmlReader Open(ZipArchiveEntry entry, Budget budget) =>
        XmlReader.Create(
            new CountingStream(entry.Open(), budget),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = false, CloseInput = true });

    private static List<SheetRef> ReadSheets(ZipArchiveEntry entry, Budget budget)
    {
        var sheets = new List<SheetRef>();
        using var xml = Open(entry, budget);
        while (xml.Read())
        {
            if (xml is { NodeType: XmlNodeType.Element, LocalName: "sheet" })
            {
                var id = xml.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships") ?? string.Empty;
                sheets.Add(new SheetRef(xml.GetAttribute("name") ?? string.Empty, id, xml.GetAttribute("state") is "hidden" or "veryHidden"));
            }
        }

        return sheets;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchiveEntry entry, Budget budget)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var xml = Open(entry, budget);
        while (xml.Read())
        {
            if (xml is { NodeType: XmlNodeType.Element, LocalName: "Relationship" } && xml.GetAttribute("Id") is { } id && xml.GetAttribute("Target") is { } target)
            {
                map[id] = target;
            }
        }

        return map;
    }

    private static List<string> ReadSharedStrings(ZipArchiveEntry entry, Budget budget)
    {
        var list = new List<string>();
        using var xml = Open(entry, budget);
        while (xml.Read())
        {
            if (xml is { NodeType: XmlNodeType.Element, LocalName: "si" } && !xml.IsEmptyElement)
            {
                list.Add(ReadTextOf(xml, "si"));
            }
        }

        return list;
    }

    // Concatenates the <t> texts inside the current element (rich text runs included), skipping phonetic hints.
    private static string ReadTextOf(XmlReader xml, string elementName)
    {
        var text = new System.Text.StringBuilder();
        var depth = xml.Depth;
        var skipDepth = -1;
        var advance = true;
        while (true)
        {
            // ReadElementContentAsString leaves the reader on the node AFTER the element, which must be examined before the next Read.
            if (advance && !xml.Read())
            {
                break;
            }

            advance = true;
            if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth && xml.LocalName == elementName)
            {
                break;
            }

            if (xml is { NodeType: XmlNodeType.Element, LocalName: "rPh" or "phoneticPr" })
            {
                skipDepth = xml.IsEmptyElement ? -1 : xml.Depth;
            }
            else if (skipDepth >= 0 && xml is { NodeType: XmlNodeType.EndElement } && xml.Depth == skipDepth)
            {
                skipDepth = -1;
            }
            else if (skipDepth < 0 && xml is { NodeType: XmlNodeType.Element, LocalName: "t" } && !xml.IsEmptyElement)
            {
                text.Append(xml.ReadElementContentAsString());
                advance = false;
            }
        }

        return text.ToString();
    }

    private static List<WorkbookRow> ReadRows(ZipArchiveEntry entry, List<string> shared, Budget budget, out bool tooMany)
    {
        tooMany = false;
        var rows = new List<WorkbookRow>();
        using var xml = Open(entry, budget);
        var fallbackRow = 0;
        while (xml.Read())
        {
            if (xml is not { NodeType: XmlNodeType.Element, LocalName: "row" })
            {
                continue;
            }

            fallbackRow++;
            var rowNumber = int.TryParse(xml.GetAttribute("r"), out var parsedRow) ? parsedRow : fallbackRow;
            fallbackRow = rowNumber;
            var cells = new Dictionary<int, WorkbookCell>();
            if (!xml.IsEmptyElement)
            {
                var rowDepth = xml.Depth;
                var nextColumn = 1;
                while (xml.Read() && !(xml.NodeType == XmlNodeType.EndElement && xml.Depth == rowDepth))
                {
                    if (xml is { NodeType: XmlNodeType.Element, LocalName: "c" })
                    {
                        var column = ColumnOf(xml.GetAttribute("r")) ?? nextColumn;
                        nextColumn = column + 1;
                        var cell = ReadCell(xml, shared);
                        if (cell is not null && cell.Text.Length > 0)
                        {
                            cells[column] = cell;
                        }
                    }
                }
            }

            if (cells.Count > 0)
            {
                rows.Add(new WorkbookRow(rowNumber, cells));
                if (rows.Count > MaxRows)
                {
                    tooMany = true;
                    return rows;
                }
            }
        }

        return rows;
    }

    private static WorkbookCell? ReadCell(XmlReader xml, List<string> shared)
    {
        var type = xml.GetAttribute("t");
        if (xml.IsEmptyElement)
        {
            return null;
        }

        var depth = xml.Depth;
        string? value = null;
        string? inline = null;
        var advance = true;
        while (true)
        {
            if (advance && !xml.Read())
            {
                break;
            }

            advance = true;
            if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth)
            {
                break;
            }

            if (xml is { NodeType: XmlNodeType.Element, LocalName: "v" } && !xml.IsEmptyElement)
            {
                value = xml.ReadElementContentAsString();
                advance = false;
            }
            else if (xml is { NodeType: XmlNodeType.Element, LocalName: "is" } && !xml.IsEmptyElement)
            {
                inline = ReadTextOf(xml, "is");
            }
        }

        return type switch
        {
            "s" => int.TryParse(value, out var index) && index >= 0 && index < shared.Count ? new WorkbookCell(shared[index].Trim(), false) : null,
            "inlineStr" => new WorkbookCell((inline ?? value ?? string.Empty).Trim(), false),
            "str" => new WorkbookCell((value ?? string.Empty).Trim(), false),
            "b" => new WorkbookCell(value == "1" ? "TRUE" : "FALSE", false),
            "e" => null,
            _ => value is null ? null : new WorkbookCell(value.Trim(), true),
        };
    }

    private static int? ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var column = 0;
        foreach (var c in reference)
        {
            if (c is >= 'A' and <= 'Z')
            {
                column = (column * 26) + (c - 'A' + 1);
            }
            else if (c is >= 'a' and <= 'z')
            {
                column = (column * 26) + (c - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return column is >= 1 and <= 16384 ? column : null;
    }
}
