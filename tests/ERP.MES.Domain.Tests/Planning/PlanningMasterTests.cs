using ERP.MES.Domain.Planning;

namespace ERP.MES.Domain.Tests.Planning;

// Production units, machines with stable codes and the append-only hourly-rate history (Decision Log #10, #82 R2, #89).
public class PlanningMasterTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("FCC-06")]
    [InlineData("TCC-12")]
    [InlineData("HUB-01")]
    public void Machine_codes_such_as_the_owners_examples_are_accepted_exactly_as_given(string code)
    {
        var machine = new Machine(0, code, "  دستگاه  ", productionUnitId: 3, createdByUserId: 5, Now);

        Assert.Equal(code, machine.Code);
        Assert.Equal("دستگاه", machine.Name);
        Assert.Equal(3, machine.ProductionUnitId);
        Assert.True(machine.IsActive);
        Assert.Equal(5, machine.CreatedByUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fcc-06")]
    [InlineData("FCC 06")]
    [InlineData("FCC.06")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void An_invalid_machine_or_unit_code_is_refused(string code)
    {
        Assert.Throws<ArgumentException>(() => new Machine(0, code, "x", 1, 1, Now));
        Assert.Throws<ArgumentException>(() => new ProductionUnit(0, code, "x", 1, Now));
    }

    [Fact]
    public void The_machine_code_and_unit_never_change_only_the_name_and_the_active_flag_do()
    {
        var machine = new Machine(0, "FCC-06", "الف", 3, 5, Now);

        machine.Rename("ب", Now.AddMinutes(1));
        machine.SetActive(false, Now.AddMinutes(2));

        Assert.Equal("FCC-06", machine.Code);
        Assert.Equal(3, machine.ProductionUnitId);
        Assert.Equal("ب", machine.Name);
        Assert.False(machine.IsActive);
        Assert.Equal(Now.AddMinutes(2), machine.UpdatedAtUtc);
        Assert.DoesNotContain(typeof(Machine).GetProperties(), p => p is { Name: "Code" or "ProductionUnitId" } && p.SetMethod is { IsPublic: true });
        Assert.Throws<ArgumentException>(() => machine.Rename(" ", Now));
        Assert.Throws<ArgumentException>(() => new Machine(0, "FCC-06", "x", 1, 1, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void A_production_unit_renames_and_retires_but_keeps_its_code()
    {
        var unit = new ProductionUnit(0, "TURNING", "تراشکاری", 5, Now);

        unit.Rename("تراشکاری CNC", Now.AddMinutes(1));
        unit.SetActive(false, Now.AddMinutes(2));

        Assert.Equal("TURNING", unit.Code);
        Assert.Equal("تراشکاری CNC", unit.Name);
        Assert.False(unit.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000_000_001)]
    public void An_hourly_rate_must_be_positive_and_within_the_sanity_bound(long rial)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachineHourlyRate(0, 1, rial, new DateOnly(2026, 10, 22), null, 5, Now));
    }

    [Fact]
    public void A_rate_keeps_its_exact_value_date_trimmed_reason_and_author()
    {
        var rate = new MachineHourlyRate(0, 7, 1_234_567.89m, new DateOnly(2026, 10, 22), "  new contract  ", 5, Now);

        Assert.Equal(1_234_567.89m, rate.RatePerHourRial);
        Assert.Equal(new DateOnly(2026, 10, 22), rate.EffectiveFrom);
        Assert.Equal("new contract", rate.Reason);
        Assert.Equal(5, rate.ChangedByUserId);
        Assert.Null(new MachineHourlyRate(0, 7, 1m, new DateOnly(2026, 10, 22), "  ", 5, Now).Reason);
        Assert.Throws<ArgumentException>(() => new MachineHourlyRate(0, 7, 1m, new DateOnly(2026, 10, 22), new string('x', 501), 5, Now));
        Assert.Throws<ArgumentException>(() => new MachineHourlyRate(0, 7, 1m, new DateOnly(2026, 10, 22), null, 5, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        Assert.DoesNotContain(typeof(MachineHourlyRate).GetProperties(), p => p.SetMethod is { IsPublic: true });
    }

    [Fact]
    public void The_rate_in_force_on_a_work_date_is_the_latest_entry_that_has_started()
    {
        var rates = new[]
        {
            new MachineHourlyRate(1, 7, 100m, new DateOnly(2026, 1, 1), null, 5, Now),
            new MachineHourlyRate(2, 7, 150m, new DateOnly(2026, 6, 1), null, 5, Now),
            new MachineHourlyRate(3, 8, 999m, new DateOnly(2026, 1, 1), null, 5, Now),
            new MachineHourlyRate(4, 7, 200m, new DateOnly(2027, 1, 1), null, 5, Now),
        };

        Assert.Null(MachineRates.RateOn(rates, 7, new DateOnly(2025, 12, 31)));
        Assert.Equal(100m, MachineRates.RateOn(rates, 7, new DateOnly(2026, 1, 1))!.RatePerHourRial);
        Assert.Equal(100m, MachineRates.RateOn(rates, 7, new DateOnly(2026, 5, 31))!.RatePerHourRial);
        Assert.Equal(150m, MachineRates.RateOn(rates, 7, new DateOnly(2026, 6, 1))!.RatePerHourRial);
        Assert.Equal(150m, MachineRates.RateOn(rates, 7, new DateOnly(2026, 12, 31))!.RatePerHourRial);
        Assert.Equal(200m, MachineRates.RateOn(rates, 7, new DateOnly(2030, 1, 1))!.RatePerHourRial);
        Assert.Equal(999m, MachineRates.RateOn(rates, 8, new DateOnly(2026, 7, 1))!.RatePerHourRial);
        Assert.Null(MachineRates.RateOn(rates, 99, new DateOnly(2026, 7, 1)));
        Assert.Throws<ArgumentNullException>(() => MachineRates.RateOn(null!, 7, new DateOnly(2026, 7, 1)));
    }
}
