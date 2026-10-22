using ERP.MES.Domain.Planning;

namespace ERP.MES.Domain.Tests.Planning;

public class OperatorTests
{
    private static readonly DateTime At = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly D1 = new(2026, 9, 23);
    private static readonly DateOnly D2 = new(2026, 10, 3);

    [Fact]
    public void An_assignment_covers_its_days_and_a_closed_one_keeps_its_history()
    {
        var a = new OperatorMachineAssignment(1, 7, 9, D1, true, "  دلیل  ", 1, At);

        Assert.True(a.IsOpen);
        Assert.Equal("دلیل", a.Reason);
        Assert.False(a.CoversDate(D1.AddDays(-1)));
        Assert.True(a.CoversDate(D2));

        a.Close(D2, 1, At);

        Assert.False(a.IsOpen);
        Assert.True(a.CoversDate(D2));
        Assert.False(a.CoversDate(D2.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => a.Close(D2, 1, At));
    }

    [Fact]
    public void An_assignment_cannot_close_before_it_starts()
    {
        var a = new OperatorMachineAssignment(1, 7, 9, D1, false, null, 1, At);

        Assert.Throws<ArgumentOutOfRangeException>(() => a.Close(D1.AddDays(-1), 1, At));
        Assert.True(a.IsOpen);
    }

    [Fact]
    public void Assignment_does_not_imply_qualification()
    {
        var assignment = new OperatorMachineAssignment(1, 7, 9, D1, true, null, 1, At);
        var qualification = new OperatorMachineQualification(1, 7, 9, D2, "گواهی", 1, At);

        Assert.True(OperatorMachineRules.IsAssigned([assignment], 7, 9, D2));
        Assert.False(OperatorMachineRules.IsQualified([], 7, 9, D2));
        Assert.False(OperatorMachineRules.IsQualified([qualification], 7, 9, D2.AddDays(-1)));
        Assert.True(OperatorMachineRules.IsQualified([qualification], 7, 9, D2));
        Assert.False(OperatorMachineRules.IsQualified([qualification], 8, 9, D2));
        Assert.False(OperatorMachineRules.IsQualified([qualification], 7, 10, D2));
    }

    [Fact]
    public void A_qualification_ends_once_and_not_before_it_starts()
    {
        var q = new OperatorMachineQualification(1, 7, 9, D1, null, 1, At);

        Assert.Throws<ArgumentOutOfRangeException>(() => q.End(D1.AddDays(-1)));
        q.End(D2);
        Assert.False(q.IsOpen);
        Assert.False(q.CoversDate(D2.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => q.End(D2));
    }

    [Fact]
    public void Text_limits_are_enforced()
    {
        var tooLong = new string('x', 501);

        Assert.Throws<ArgumentException>(() => new OperatorMachineAssignment(1, 7, 9, D1, true, tooLong, 1, At));
        Assert.Throws<ArgumentException>(() => new OperatorMachineQualification(1, 7, 9, D1, tooLong, 1, At));
    }
}
