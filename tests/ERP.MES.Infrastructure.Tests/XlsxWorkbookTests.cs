using System.IO.Compression;
using System.Text;
using ERP.MES.Application.Imports;
using ERP.MES.Infrastructure.Imports;

namespace ERP.MES.Infrastructure.Tests;

// The own xlsx reader/writer (no spreadsheet package, Decision Log #97) must round-trip text, refuse hostile files and report numeric cells.
public class XlsxWorkbookTests
{
    private static readonly XlsxWorkbookReader Reader = new();
    private static readonly XlsxWorkbookWriter Writer = new();

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }

        return stream.ToArray();
    }

    private const string Types = "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>";
    private const string Workbook = "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
    private const string Rels = "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"x\" Target=\"worksheets/sheet1.xml\"/></Relationships>";

    [Fact]
    public void Text_with_persian_letters_special_characters_and_leading_zeros_round_trips_exactly()
    {
        var bytes = Writer.Write("آیتم‌ها", ["شماره آیتم", "نام"], [["1.10", "حلقه <A> & \"B\""], ["007", "  فاصله  "]]);

        var result = Reader.Read(bytes);

        Assert.Equal(WorkbookError.None, result.Error);
        Assert.Equal(1, result.SheetCount);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("شماره آیتم", result.Rows[0].Cells[1].Text);
        Assert.Equal("1.10", result.Rows[1].Cells[1].Text);
        Assert.False(result.Rows[1].Cells[1].IsNumber);
        Assert.Equal("حلقه <A> & \"B\"", result.Rows[1].Cells[2].Text);
        Assert.Equal("007", result.Rows[2].Cells[1].Text);
        Assert.Equal("فاصله", result.Rows[2].Cells[2].Text); // cell text is trimmed (documented)
    }

    [Fact]
    public void Numeric_cells_are_reported_as_numbers_and_shared_strings_are_resolved()
    {
        var sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"C1\"><v>1.1</v></c></row></sheetData></worksheet>";
        var shared = "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>متن</t></si></sst>";
        var result = Reader.Read(Zip(("[Content_Types].xml", Types), ("xl/workbook.xml", Workbook), ("xl/_rels/workbook.xml.rels", Rels), ("xl/worksheets/sheet1.xml", sheet), ("xl/sharedStrings.xml", shared)));

        Assert.Equal(WorkbookError.None, result.Error);
        var row = Assert.Single(result.Rows);
        Assert.Equal("متن", row.Cells[1].Text);
        Assert.False(row.Cells[1].IsNumber);
        Assert.Equal("1.1", row.Cells[3].Text);
        Assert.True(row.Cells[3].IsNumber);
    }

    [Fact]
    public void Files_that_are_not_xlsx_are_refused()
    {
        Assert.Equal(WorkbookError.NotAnXlsxFile, Reader.Read(Encoding.UTF8.GetBytes("a,b\n1,2")).Error);
        Assert.Equal(WorkbookError.NotAnXlsxFile, Reader.Read([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]).Error);
        Assert.Equal(WorkbookError.NotAnXlsxFile, Reader.Read([]).Error);
        Assert.Equal(WorkbookError.NotAnXlsxFile, Reader.Read(Zip(("readme.txt", "hello"))).Error);
        Assert.Equal(WorkbookError.NotAnXlsxFile, Reader.Read(Encoding.ASCII.GetBytes("PK\u0003\u0004 not really a zip")).Error);
    }

    [Fact]
    public void A_zip_bomb_is_refused_by_the_expanded_size_budget_not_the_declared_size()
    {
        var huge = new string(' ', 1024 * 1024);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", Types);
            Add(zip, "xl/workbook.xml", Workbook);
            Add(zip, "xl/_rels/workbook.xml.rels", Rels);
            using var w = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>");
            for (var i = 0; i < 120; i++)
            {
                w.Write(huge);
            }

            w.Write("</t></is></c></row></sheetData></worksheet>");
        }

        var bytes = stream.ToArray();
        Assert.True(bytes.Length < 2 * 1024 * 1024, "the bomb must be small on disk");
        Assert.Equal(WorkbookError.TooLarge, Reader.Read(bytes).Error);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    [Fact]
    public void A_document_type_declaration_is_refused_so_external_entities_cannot_be_used()
    {
        var evil = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>&e;</t></is></c></row></sheetData></worksheet>";

        var result = Reader.Read(Zip(("[Content_Types].xml", Types), ("xl/workbook.xml", Workbook), ("xl/_rels/workbook.xml.rels", Rels), ("xl/worksheets/sheet1.xml", evil)));

        Assert.Equal(WorkbookError.Corrupt, result.Error);
    }

    [Fact]
    public void Too_many_rows_or_entries_and_a_missing_sheet_are_reported()
    {
        var rows = Enumerable.Range(0, XlsxWorkbookReader.MaxRows + 5).Select(i => (IReadOnlyList<string>)[i.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ToList();
        Assert.Equal(WorkbookError.TooManyRows, Reader.Read(Writer.Write("S", ["h"], rows)).Error);

        var entries = Enumerable.Range(0, 600).Select(i => ($"x/{i}.txt", "a")).ToArray();
        Assert.Equal(WorkbookError.TooLarge, Reader.Read(Zip(entries)).Error);

        var noSheets = "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets/></workbook>";
        Assert.Equal(WorkbookError.NoWorksheet, Reader.Read(Zip(("xl/workbook.xml", noSheets))).Error);
    }

    [Fact]
    public void The_canonical_template_is_a_readable_workbook_with_all_headers_and_both_example_rows()
    {
        var bytes = new XlsxWorkbookWriter().Write("آیتم‌ها", ItemImportTemplate.Headers, ItemImportTemplate.ExampleRows);

        var result = Reader.Read(bytes);

        Assert.Equal(WorkbookError.None, result.Error);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(ItemImportTemplate.Headers.Count, result.Rows[0].Cells.Count);
        Assert.Equal(ItemImportTemplate.ItemNo, result.Rows[0].Cells[2].Text);
        Assert.Equal("1.10", result.Rows[1].Cells[2].Text);
    }
}
