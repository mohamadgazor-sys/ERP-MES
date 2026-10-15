namespace ERP.MES.Domain.Requests;

/// <summary>
/// Priority levels (Domain and Database Specification section 18, Decision Log / Master Specification).
/// The numeric value is the sort order: 1 is the highest urgency. Several items may share one priority.
/// </summary>
public enum Priority
{
    VeryUrgent = 1,
    Urgent = 2,
    Normal = 3,
}

/// <summary>Stable code and Persian display text of each <see cref="Priority"/>.</summary>
public static class PriorityInfo
{
    public static IReadOnlyList<Priority> All { get; } = [Priority.VeryUrgent, Priority.Urgent, Priority.Normal];

    public static int SortOrder(this Priority priority) => (int)Validate(priority);

    public static string Code(this Priority priority) => Validate(priority) switch
    {
        Priority.VeryUrgent => "VERY_URGENT",
        Priority.Urgent => "URGENT",
        _ => "NORMAL",
    };

    /// <summary>The exact labels required by the specification: "خیلی فوری", "فوری", "عادی" (written as escapes so the code is encoding-proof).</summary>
    public static string PersianName(this Priority priority) => Validate(priority) switch
    {
        Priority.VeryUrgent => "خیلی فوری",
        Priority.Urgent => "فوری",
        _ => "عادی",
    };

    /// <summary>Negative when <paramref name="a"/> is more urgent than <paramref name="b"/>; zero when they share a priority.</summary>
    public static int CompareUrgency(Priority a, Priority b) => a.SortOrder().CompareTo(b.SortOrder());

    private static Priority Validate(Priority priority) =>
        Enum.IsDefined(priority) ? priority : throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
}
