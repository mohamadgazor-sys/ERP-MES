using System.IO.Compression;
using System.Text;
using ERP.MES.Application.Imports;

namespace ERP.MES.Infrastructure.Imports;

/// <summary>Writes a minimal one-sheet .xlsx (right-to-left sheet, every cell as text) with the .NET base libraries only. Used for the canonical import template and for tests.</summary>
public sealed class XlsxWorkbookWriter : IWorkbookWriter
{
    public byte[] Write(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
                """);
            AddEntry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            AddEntry(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{Escape(sheetName)}" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            AddEntry(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);

            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews><sheetData>");
            AppendRow(sheet, 1, headers);
            for (var i = 0; i < rows.Count; i++)
            {
                AppendRow(sheet, i + 2, rows[i]);
            }

            sheet.Append("</sheetData></worksheet>");
            AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return stream.ToArray();
    }

    private static void AppendRow(StringBuilder sheet, int rowNumber, IReadOnlyList<string> cells)
    {
        sheet.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var i = 0; i < cells.Count; i++)
        {
            if (string.IsNullOrEmpty(cells[i]))
            {
                continue;
            }

            sheet.Append("<c r=\"").Append(ColumnName(i + 1)).Append(rowNumber).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Escape(cells[i])).Append("</t></is></c>");
        }

        sheet.Append("</row>");
    }

    private static string ColumnName(int column)
    {
        var name = new StringBuilder();
        while (column > 0)
        {
            var rest = (column - 1) % 26;
            name.Insert(0, (char)('A' + rest));
            column = (column - 1) / 26;
        }

        return name.ToString();
    }

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? string.Empty;

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }
}
