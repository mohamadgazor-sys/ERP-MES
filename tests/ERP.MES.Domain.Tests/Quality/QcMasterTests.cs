using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Tests.Quality;

// Units of measure, conversions and quantity types (Decision Log #9, #90): stable codes, derived reciprocals, explicit set composition, immutable mappings.
public class QcMasterTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    // ---- units of measure ----

    [Theory]
    [InlineData("PCS")]
    [InlineData("KG")]
    [InlineData("M-2")]
    [InlineData("UNIT_1")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    public void A_valid_code_is_accepted_exactly_as_given(string code)
    {
        var unit = new UnitOfMeasure(0, code, "  نام  ", UnitKind.Count, 5, Now);

        Assert.Equal(code, unit.Code);
        Assert.Equal("نام", unit.Name);
        Assert.True(unit.IsActive);
        Assert.Equal(5, unit.CreatedByUserId);
        Assert.Equal(Now, unit.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pcs")]
    [InlineData("P CS")]
    [InlineData("پی")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    [InlineData("A.B")]
    public void An_invalid_code_is_refused(string code)
    {
        Assert.False(UnitOfMeasure.IsValidCode(code));
        Assert.Throws<ArgumentException>(() => new UnitOfMeasure(0, code, "نام", UnitKind.Count, 1, Now));
    }

    [Fact]
    public void A_name_a_known_kind_and_a_utc_time_are_required()
    {
        Assert.Throws<ArgumentException>(() => new UnitOfMeasure(0, "KG", " ", UnitKind.Mass, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitOfMeasure(0, "KG", "کیلو", (UnitKind)0, 1, Now));
        Assert.Throws<ArgumentException>(() => new UnitOfMeasure(0, "KG", "کیلو", UnitKind.Mass, 1, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        Assert.Null(new UnitOfMeasure(0, "KG", "کیلو", UnitKind.Mass, null, Now).CreatedByUserId);
    }

    [Fact]
    public void Rename_and_deactivate_change_the_name_and_the_flag_but_never_the_code_or_kind()
    {
        var unit = new UnitOfMeasure(0, "PCS", "قطعه", UnitKind.Count, 1, Now);

        unit.Rename("عدد", Now.AddMinutes(1));
        unit.SetActive(false, Now.AddMinutes(2));

        Assert.Equal("PCS", unit.Code);
        Assert.Equal("عدد", unit.Name);
        Assert.Equal(UnitKind.Count, unit.Kind);
        Assert.False(unit.IsActive);
        Assert.Equal(Now.AddMinutes(2), unit.UpdatedAtUtc);
        Assert.Throws<ArgumentException>(() => unit.Rename(" ", Now));
        Assert.Throws<ArgumentException>(() => unit.SetActive(true, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    // ---- conversions ----

    [Fact]
    public void A_conversion_needs_two_different_units_and_a_positive_factor()
    {
        Assert.Throws<ArgumentException>(() => new UnitConversion(0, 1, 1, 2m, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitConversion(0, 1, 2, 0m, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitConversion(0, 1, 2, -3m, 1, Now));
        Assert.Equal(0.000001m, new UnitConversion(0, 1, 2, 0.000001m, 1, Now).Factor);
    }

    [Fact]
    public void The_reciprocal_is_derived_automatically_pair_to_piece_2_gives_piece_to_pair_0_5()
    {
        var pairToPiece = new UnitConversion(1, 2, 1, 2m, null, Now);

        Assert.True(UnitConversions.TryResolve([pairToPiece], from: 2, to: 1, out var forward));
        Assert.True(UnitConversions.TryResolve([pairToPiece], from: 1, to: 2, out var backward));

        Assert.Equal(2m, forward);
        Assert.Equal(0.5m, backward);
    }

    [Fact]
    public void A_unit_converts_to_itself_with_factor_one_and_unrelated_units_do_not_convert()
    {
        var conversion = new UnitConversion(1, 2, 1, 2m, null, Now);

        Assert.True(UnitConversions.TryResolve([], 7, 7, out var same));
        Assert.Equal(1m, same);
        Assert.False(UnitConversions.TryResolve([conversion], 1, 3, out var none));
        Assert.Equal(0m, none);

        // No chaining through a third unit: 2 -> 1 and 1 -> 3 do not give 2 -> 3.
        var second = new UnitConversion(2, 1, 3, 10m, null, Now);
        Assert.False(UnitConversions.TryResolve([conversion, second], 2, 3, out _));
    }

    [Fact]
    public void A_deactivated_conversion_is_not_used()
    {
        var conversion = new UnitConversion(1, 2, 1, 2m, null, Now);
        conversion.SetActive(false, Now.AddMinutes(1));

        Assert.False(UnitConversions.TryResolve([conversion], 2, 1, out _));
        Assert.False(UnitConversions.TryResolve([conversion], 1, 2, out _));
        Assert.Throws<ArgumentNullException>(() => UnitConversions.TryResolve(null!, 1, 2, out _));
    }

    // ---- quantity types ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    public void A_simple_quantity_type_maps_to_the_base_counting_unit(int baseUnits)
    {
        var type = QuantityType.CreateSimple(0, "PAIR", "جفت", baseUnitId: 1, baseUnits, 5, Now);

        Assert.Equal(baseUnits, type.BaseUnitsPerType);
        Assert.Equal(1, type.BaseUnitId);
        Assert.False(type.IsSet);
        Assert.Empty(type.Members);
        Assert.True(type.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_simple_quantity_type_counts_at_least_one_base_unit(int baseUnits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityType.CreateSimple(0, "X", "x", 1, baseUnits, 1, Now));
    }

    [Fact]
    public void A_set_has_an_explicit_composition_and_counts_the_sum_of_its_members()
    {
        var set = QuantityType.CreateSet(0, "SET-A", "ست الف", 1, [("شفت", 1), ("بوش", 2), ("واشر", 4)], 5, Now);

        Assert.True(set.IsSet);
        Assert.Equal(7, set.BaseUnitsPerType);
        Assert.Equal(new[] { 1, 2, 3 }, set.Members.Select(m => m.MemberNo).ToArray());
        Assert.Equal(new[] { "شفت", "بوش", "واشر" }, set.Members.Select(m => m.Description).ToArray());
        Assert.Equal(new[] { 1, 2, 4 }, set.Members.Select(m => m.BaseUnits).ToArray());
    }

    [Fact]
    public void Two_sets_are_never_assumed_alike()
    {
        var a = QuantityType.CreateSet(0, "SET-A", "الف", 1, [("p", 1), ("q", 1)], 1, Now);
        var b = QuantityType.CreateSet(0, "SET-B", "ب", 1, [("p", 1), ("q", 3)], 1, Now);

        Assert.Equal(2, a.BaseUnitsPerType);
        Assert.Equal(4, b.BaseUnitsPerType);
    }

    [Fact]
    public void A_set_needs_two_members_each_described_and_counting_at_least_one()
    {
        Assert.Throws<ArgumentException>(() => QuantityType.CreateSet(0, "S", "s", 1, [("only", 3)], 1, Now));
        Assert.Throws<ArgumentException>(() => QuantityType.CreateSet(0, "S", "s", 1, [], 1, Now));
        Assert.Throws<ArgumentException>(() => QuantityType.CreateSet(0, "S", "s", 1, [("a", 1), (" ", 1)], 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityType.CreateSet(0, "S", "s", 1, [("a", 1), ("b", 0)], 1, Now));
        Assert.Throws<ArgumentNullException>(() => QuantityType.CreateSet(0, "S", "s", 1, null!, 1, Now));
    }

    [Fact]
    public void Only_the_name_and_the_active_flag_of_a_quantity_type_can_change_its_mapping_is_fixed()
    {
        var type = QuantityType.CreateSimple(0, "PAIR", "جفت", 1, 2, 5, Now);

        type.Rename("جفت (دوتایی)", Now.AddMinutes(1));
        type.SetActive(false, Now.AddMinutes(2));

        Assert.Equal("جفت (دوتایی)", type.Name);
        Assert.False(type.IsActive);
        Assert.Equal(2, type.BaseUnitsPerType);
        Assert.Equal("PAIR", type.Code);
        Assert.DoesNotContain(typeof(QuantityType).GetProperties(), p => p.SetMethod is { IsPublic: true });
        Assert.Throws<ArgumentException>(() => type.Rename(" ", Now));
        Assert.Throws<ArgumentException>(() => QuantityType.CreateSimple(0, "bad code", "x", 1, 1, 1, Now));
    }
}
