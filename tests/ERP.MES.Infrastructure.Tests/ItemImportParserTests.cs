using ERP.MES.Application.Imports;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// The pure parser: headers by name, per-row errors with row numbers, text-only item numbers, stage groups (Decision Log #97).
public class ItemImportParserTests
{
    private static readonly ImportLookups Lookups = new(
        new Dictionary<string, long> { ["PIECE"] = 1 },
        new Dictionary<string, long> { ["ST37"] = 7 },
        new Dictionary<string, long> { ["MILL"] = 3, ["TURN"] = 4 },
        new Dictionary<string, (long Id, long UnitId)> { ["M1"] = (10, 3) });

    private static WorkbookRow Row(int n, params string[] cells) =>
        new(n, cells.Select((c, i) => (c, i)).Where(t => t.c.Length > 0).ToDictionary(t => t.i + 1, t => new WorkbookCell(t.c, false)));

    private static WorkbookRow Row(int n, string[] first, params string[] more) => Row(n, [.. first, .. more]);

    private static WorkbookReadResult Book(params WorkbookRow[] rows) => new(WorkbookError.None, rows, 1);

    private static readonly string[] Header = [ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.QuantityType, ItemImportTemplate.Material, ItemImportTemplate.Weight];

    [Fact]
    public void Headers_are_matched_by_name_in_any_order_with_arabic_letters_digits_and_spacing_normalized()
    {
        var book = Book(
            Row(1, "  تعداد ", "نام  قطعه", "شماره آیتم", "كد نوع تعداد", "کد ماده", "وزن هر واحد پایه (کیلوگرم)"),
            Row(2, "۱۲", "حلقه", "1.10", "PIECE", "ST37", "۱٫۵".Replace('٫', '.')));

        var parsed = ItemImportParser.Parse(book, WorkType.Make, Lookups);

        Assert.True(parsed.FileOk, string.Join(",", parsed.FileMessages.Select(m => m.Issue)));
        var data = Assert.Single(parsed.Rows).Data!;
        Assert.Equal("1.10", data.ItemNo);
        Assert.Equal(12m, data.Quantity);
        Assert.Equal(1.5m, data.Specification.WeightKgPerBaseUnit);
    }

    [Fact]
    public void A_numeric_item_number_cell_is_an_error_because_it_would_lose_trailing_zeros()
    {
        var numeric = new WorkbookRow(2, new Dictionary<int, WorkbookCell>
        {
            [1] = new("1.1", true), [2] = new("حلقه", false), [3] = new("2", false), [4] = new("PIECE", false), [5] = new("ST37", false), [6] = new("1", false),
        });

        var row = Assert.Single(ItemImportParser.Parse(Book(Row(1, Header), numeric), WorkType.Make, Lookups).Rows);

        Assert.Null(row.Data);
        Assert.Contains(row.Messages, m => m.Issue == ImportIssue.ItemNoNumeric && m.SheetRow == 2);
    }

    [Fact]
    public void Item_numbers_are_compared_case_sensitively_so_a_and_lowercase_a_are_different_items()
    {
        var parsed = ItemImportParser.Parse(Book(Row(1, Header), Row(2, "A-1", "x", "1", "PIECE", "ST37", "1"), Row(3, "a-1", "y", "1", "PIECE", "ST37", "1")), WorkType.Make, Lookups);

        Assert.All(parsed.Rows, r => Assert.NotNull(r.Data));
    }

    [Fact]
    public void Unknown_columns_warn_but_do_not_block_and_duplicate_or_missing_required_headers_do()
    {
        var withExtra = ItemImportParser.Parse(Book(Row(1, Header, "ستون اضافه"), Row(2, "1", "x", "1", "PIECE", "ST37", "1")), WorkType.Make, Lookups);
        Assert.True(withExtra.FileOk);
        Assert.Contains(withExtra.FileMessages, m => m.Issue == ImportIssue.HeaderUnknown && !m.IsError);

        var dup = ItemImportParser.Parse(Book(Row(1, Header, ItemImportTemplate.ItemNo)), WorkType.Make, Lookups);
        Assert.Contains(dup.FileMessages, m => m.Issue == ImportIssue.HeaderDuplicate && m.IsError);

        var missing = ItemImportParser.Parse(Book(Row(1, ItemImportTemplate.ItemNo)), WorkType.Make, Lookups);
        Assert.Contains(missing.FileMessages, m => m.Issue == ImportIssue.HeaderMissing && m.IsError);

        Assert.Contains(ItemImportParser.Parse(Book(), WorkType.Make, Lookups).FileMessages, m => m.IsError);
    }

    [Fact]
    public void Stage_groups_must_be_complete_contiguous_and_use_known_codes()
    {
        string[] header = [.. Header, ItemImportTemplate.StageTitle(1), ItemImportTemplate.StageMinutes(1), ItemImportTemplate.StageUnit(1), ItemImportTemplate.StageMachine(1), ItemImportTemplate.StageTitle(2), ItemImportTemplate.StageMinutes(2), ItemImportTemplate.StageUnit(2)];
        string[] baseCells = ["1", "x", "1", "PIECE", "ST37", "1"];

        var ok = ItemImportParser.Parse(Book(Row(1, header), Row(2, baseCells, "برش", "5", "MILL", "M1", "", "", "")), WorkType.Make, Lookups).Rows.Single();
        Assert.Equal(1, ok.Data!.Stages.Count);
        Assert.Equal(10, ok.Data.Stages[0].MachineId);

        var gap = ItemImportParser.Parse(Book(Row(1, header), Row(2, baseCells, "", "", "", "", "دوم", "3", "TURN")), WorkType.Make, Lookups).Rows.Single();
        Assert.Contains(gap.Messages, m => m.Issue == ImportIssue.StageGap);

        var partial = ItemImportParser.Parse(Book(Row(1, header), Row(2, baseCells, "برش", "", "MILL", "")), WorkType.Make, Lookups).Rows.Single();
        Assert.Contains(partial.Messages, m => m.Issue == ImportIssue.StageIncomplete);

        var badUnit = ItemImportParser.Parse(Book(Row(1, header), Row(2, baseCells, "برش", "5", "NOPE", "")), WorkType.Make, Lookups).Rows.Single();
        Assert.Contains(badUnit.Messages, m => m.Issue == ImportIssue.StageUnitUnknown);

        var badMinutes = ItemImportParser.Parse(Book(Row(1, header), Row(2, baseCells, "برش", "0", "MILL", "")), WorkType.Make, Lookups).Rows.Single();
        Assert.Contains(badMinutes.Messages, m => m.Issue == ImportIssue.StageMinutesInvalid);

        var mismatch = ItemImportParser.Parse(Book(Row(1, header), Row(2, baseCells, "برش", "5", "TURN", "M1")), WorkType.Make, Lookups).Rows.Single();
        Assert.Contains(mismatch.Messages, m => m.Issue == ImportIssue.StageMachineUnitMismatch);
    }

    [Fact]
    public void A_make_row_needs_quantity_type_material_and_weight_but_a_repair_row_may_omit_them()
    {
        var make = ItemImportParser.Parse(Book(Row(1, Header), Row(2, "1", "x", "1")), WorkType.Make, Lookups).Rows.Single();
        Assert.Contains(make.Messages, m => m.Issue == ImportIssue.QuantityTypeMissing);
        Assert.Contains(make.Messages, m => m.Issue == ImportIssue.MaterialMissing);
        Assert.Contains(make.Messages, m => m.Issue == ImportIssue.WeightMissing);

        var repair = ItemImportParser.Parse(Book(Row(1, Header), Row(2, "1", "x", "1")), WorkType.Repair, Lookups).Rows.Single();
        Assert.Null(repair.Data);
        Assert.Contains(repair.Messages, m => m.Issue == ImportIssue.RepairFieldsMissing);
        Assert.DoesNotContain(repair.Messages, m => m.Issue is ImportIssue.QuantityTypeMissing or ImportIssue.MaterialMissing or ImportIssue.WeightMissing);
    }

    private static readonly string[] RepairHeader = [ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.DeliveryDate, ItemImportTemplate.WarehouseCode];
    private static readonly DateOnly RequestDate = new(2026, 5, 6);

    [Fact]
    public void A_repair_row_needs_a_jalali_delivery_date_on_or_after_the_request_date_and_a_warehouse_code_of_at_most_forty_characters()
    {
        ImportRowOutcome Parse(string delivery, string code) => new(ItemImportParser.Parse(Book(Row(1, RepairHeader), Row(2, "R-1", "Shaft", "1", delivery, code)), WorkType.Repair, Lookups, RequestDate).Rows.Single());

        var ok = Parse("1405/02/16", "WH-77");
        Assert.NotNull(ok.Row.Data);
        Assert.Equal(new DateOnly(2026, 5, 6), ok.Row.Data!.Specification.DeliveryDate);
        Assert.Equal("WH-77", ok.Row.Data.Specification.WarehouseItemCode);

        Assert.Contains(Parse("1405/02/15", "WH-77").Row.Messages, m => m.Issue == ImportIssue.DeliveryBeforeRequest);
        Assert.Contains(Parse("2026-05-10", "WH-77").Row.Messages, m => m.Issue == ImportIssue.DeliveryDateInvalid);
        Assert.Contains(Parse("1405/02/16", new string('W', 41)).Row.Messages, m => m.Issue == ImportIssue.WarehouseCodeInvalid);
        Assert.NotNull(Parse("1405/02/16", new string('W', 40)).Row.Data);
        Assert.Contains(Parse("", "WH-77").Row.Messages, m => m.Issue == ImportIssue.RepairFieldsMissing);
        Assert.Contains(Parse("1405/02/16", "").Row.Messages, m => m.Issue == ImportIssue.RepairFieldsMissing);
    }

    [Fact]
    public void A_make_row_does_not_need_the_repair_fields_but_checks_them_when_given()
    {
        var header = new[] { ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.QuantityType, ItemImportTemplate.Material, ItemImportTemplate.Weight, ItemImportTemplate.DeliveryDate };
        var without = ItemImportParser.Parse(Book(Row(1, header), Row(2, "M-1", "x", "1", "PIECE", "ST37", "1", "")), WorkType.Make, Lookups, RequestDate).Rows.Single();
        Assert.NotNull(without.Data);
        var early = ItemImportParser.Parse(Book(Row(1, header), Row(2, "M-1", "x", "1", "PIECE", "ST37", "1", "1405/02/01")), WorkType.Make, Lookups, RequestDate).Rows.Single();
        Assert.Contains(early.Messages, m => m.Issue == ImportIssue.DeliveryBeforeRequest);
    }

    private readonly record struct ImportRowOutcome(ParsedImportRow Row);
}
