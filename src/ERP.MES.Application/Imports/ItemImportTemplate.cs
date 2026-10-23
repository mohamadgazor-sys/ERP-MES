using System.Text;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Imports;

/// <summary>
/// The canonical item-import template (docs/ITEM_IMPORT_TEMPLATE.md; Decision Log #16, #97): ONE worksheet, Persian headers matched by NAME (not position), one row per item. It is the
/// application\'s own layout, designed from the requirements; it is NOT claimed to match any existing company workbook, which has not been inspected. Columns may appear in any order and
/// unknown columns are ignored with a warning. Stage columns come in groups of four for up to <see cref="MaxStages"/> stages.
/// </summary>
public static class ItemImportTemplate
{
    public const int MaxStages = 5;

    public const string WorkType = "نوع کار";
    public const string ItemNo = "شماره آیتم";
    public const string PartName = "نام قطعه";
    public const string Quantity = "تعداد";
    public const string QuantityType = "کد نوع تعداد";
    public const string Material = "کد ماده";
    public const string Weight = "وزن هر واحد پایه (کیلوگرم)";
    public const string Length = "طول (میلی‌متر)";
    public const string Diameter = "قطر (میلی‌متر)";
    public const string Priority = "اولویت";

    public static string StageTitle(int stage) => $"مرحله {stage} - عنوان";

    public static string StageMinutes(int stage) => $"مرحله {stage} - دقیقه برای هر قطعه";

    public static string StageUnit(int stage) => $"مرحله {stage} - کد واحد";

    public static string StageMachine(int stage) => $"مرحله {stage} - کد ماشین";

    /// <summary>The headers in template order.</summary>
    public static IReadOnlyList<string> Headers { get; } = BuildHeaders();

    /// <summary>Headers a file must contain (the other columns are optional, though a MAKE item needs the specification columns).</summary>
    public static IReadOnlyList<string> RequiredHeaders { get; } = [ItemNo, PartName, Quantity];

    /// <summary>Two FICTIONAL example rows (one MAKE with a two-stage route, one without stages). The codes must exist in the target database for the example to import.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ExampleRows { get; } =
    [
        Row("ساخت", "1.10", "حلقه فولادی", "10", "PIECE", "ST37-R30", "1.2345", "6000", "30", "فوری", ("برش", "5", "SAW", ""), ("فرزکاری", "12.5", "MILLING", "FCC-06")),
        Row("ساخت", "1.20", "شفت", "4", "PIECE", "ST37-R30", "3.5", "800", "40", "عادی"),
    ];

    /// <summary>Normalizes a header or label for comparison: trims, unifies Arabic and Persian letters and digits, removes the zero-width non-joiner and collapses white space.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastSpace = false;
        foreach (var c in text.Trim())
        {
            var mapped = c switch
            {
                'ي' => 'ی',
                'ك' => 'ک',
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c,
            };
            if (mapped is '‌' or '​' or '‎' or '‏')
            {
                continue;
            }

            if (char.IsWhiteSpace(mapped))
            {
                if (!lastSpace)
                {
                    builder.Append(' ');
                }

                lastSpace = true;
                continue;
            }

            lastSpace = false;
            builder.Append(mapped);
        }

        return builder.ToString();
    }

    public static string PriorityLabel(Priority priority) => priority.PersianName();

    private static IReadOnlyList<string> BuildHeaders()
    {
        var headers = new List<string> { WorkType, ItemNo, PartName, Quantity, QuantityType, Material, Weight, Length, Diameter, Priority };
        for (var stage = 1; stage <= MaxStages; stage++)
        {
            headers.AddRange([StageTitle(stage), StageMinutes(stage), StageUnit(stage), StageMachine(stage)]);
        }

        return headers;
    }

    private static IReadOnlyList<string> Row(
        string workType, string itemNo, string partName, string quantity, string typeCode, string materialCode, string weight, string length, string diameter, string priority,
        params (string Title, string Minutes, string Unit, string Machine)[] stages)
    {
        var cells = new List<string> { workType, itemNo, partName, quantity, typeCode, materialCode, weight, length, diameter, priority };
        for (var i = 0; i < MaxStages; i++)
        {
            if (i < stages.Length)
            {
                cells.AddRange([stages[i].Title, stages[i].Minutes, stages[i].Unit, stages[i].Machine]);
            }
            else
            {
                cells.AddRange(["", "", "", ""]);
            }
        }

        return cells;
    }
}
