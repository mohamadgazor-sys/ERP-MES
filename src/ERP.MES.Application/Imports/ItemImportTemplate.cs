using System.Text;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Imports;

/// <summary>
/// The canonical item-import template (docs/ITEM_IMPORT_TEMPLATE.md; Decision Log #16, #97): ONE worksheet, Persian headers matched by NAME (not position), one row per item. It is the
/// application\'s own layout, designed from the requirements; it is NOT claimed to match any existing company workbook, which has not been inspected. Columns may appear in any order and
/// unknown columns are ignored with a warning. Stage columns come in groups of four: the generated template shows <see cref="TemplateStages"/> groups, and a file may use up to
/// <see cref="MaxStages"/> (a safety limit, not a business rule). A stage header beyond that limit is an error, never silently ignored.
/// </summary>
public static class ItemImportTemplate
{
    /// <summary>Largest stage number a file may use (safety limit; Decision Log #98).</summary>
    public const int MaxStages = 20;

    /// <summary>How many stage groups the generated template shows.</summary>
    public const int TemplateStages = 6;

    public const string WorkType = "نوع کار";
    /// <summary>The customer's own item number (Decision Log #121 Q2): optional, and the only key a re-import matches existing items by.</summary>
    public const string CustomerItemRef = "شماره آیتم مشتری";

    /// <summary>The header files used before the system generated the item number; accepted as an alias of <see cref="CustomerItemRef"/>.</summary>
    public const string ItemNo = "شماره آیتم";

    /// <summary>The customer reference of the assembly this item belongs to (the parent must come earlier in the file or already exist in the request).</summary>
    public const string ParentRef = "مرجع آیتم والد";

    /// <summary>"بله" when the item is an assembly that may have children.</summary>
    public const string Assembly = "مونتاژ";
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
    public static IReadOnlyList<string> Headers { get; } = BuildHeaders(TemplateStages);

    /// <summary>Every header a file may contain (the template headers plus stage groups up to <see cref="MaxStages"/>).</summary>
    public static IReadOnlyList<string> AcceptedHeaders { get; } = BuildHeaders(MaxStages).Append(ItemNo).ToList();

    /// <summary>Headers a file must contain (the other columns are optional, though a MAKE item needs the specification columns).</summary>
    public static IReadOnlyList<string> RequiredHeaders { get; } = [PartName, Quantity];

    /// <summary>Two FICTIONAL example rows (one MAKE with a two-stage route, one without stages). The codes must exist in the target database for the example to import.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ExampleRows { get; } =
    [
        Row("ساخت", "1.10", "حلقه فولادی", "10", "PIECE", "ST37-R30", "1.2345", "6000", "30", "فوری", "", "", ("برش", "5", "SAW", ""), ("فرزکاری", "12.5", "MILLING", "FCC-06")),
        Row("ساخت", "1.20", "شفت", "4", "PIECE", "ST37-R30", "3.5", "800", "40", "عادی", "", ""),
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

    /// <summary>True when the (normalized) header names a stage column with a number above <see cref="MaxStages"/>.</summary>
    public static bool IsStageHeaderBeyondLimit(string normalizedHeader)
    {
        var match = System.Text.RegularExpressions.Regex.Match(normalizedHeader, @"^مرحله (\d{1,6}) - ", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > MaxStages;
    }

    public static string PriorityLabel(Priority priority) => priority.PersianName();

    private static IReadOnlyList<string> BuildHeaders(int stages)
    {
        var headers = new List<string> { WorkType, CustomerItemRef, PartName, Quantity, QuantityType, Material, Weight, Length, Diameter, Priority, ParentRef, Assembly };
        for (var stage = 1; stage <= stages; stage++)
        {
            headers.AddRange([StageTitle(stage), StageMinutes(stage), StageUnit(stage), StageMachine(stage)]);
        }

        return headers;
    }

    private static IReadOnlyList<string> Row(
        string workType, string customerRef, string partName, string quantity, string typeCode, string materialCode, string weight, string length, string diameter, string priority, string parentRef, string assembly,
        params (string Title, string Minutes, string Unit, string Machine)[] stages)
    {
        var cells = new List<string> { workType, customerRef, partName, quantity, typeCode, materialCode, weight, length, diameter, priority, parentRef, assembly };
        for (var i = 0; i < TemplateStages; i++)
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
