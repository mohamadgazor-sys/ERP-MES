using ERP.MES.Domain.Materials;

namespace ERP.MES.Domain.Tests.Planning;

public class MaterialTests
{
    private static readonly DateTime At = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_material_keeps_its_code_and_trims_the_descriptors()
    {
        var m = new Material(1, "ST37-R30", "میلگرد", "  ST37 ", " ", 1, At);

        Assert.Equal("ST37-R30", m.Code);
        Assert.Equal("ST37", m.Grade);
        Assert.Null(m.Form);
        Assert.True(m.IsActive);

        m.Update("نام جدید", null, "لوله", At.AddHours(1));
        m.SetActive(false, At.AddHours(2));

        Assert.Equal("ST37-R30", m.Code);
        Assert.Null(m.Grade);
        Assert.Equal("لوله", m.Form);
        Assert.False(m.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("st37")]
    [InlineData("A B")]
    public void An_invalid_code_is_refused(string code) => Assert.Throws<ArgumentException>(() => new Material(1, code, "x", null, null, 1, At));

    [Fact]
    public void A_price_must_be_positive_and_bounded_and_descriptors_are_limited()
    {
        var date = new DateOnly(2026, 9, 23);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialPrice(1, 1, 0m, date, null, 1, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialPrice(1, 1, MaterialPrice.MaxPricePerKgRial + 1, date, null, 1, At));
        Assert.Throws<ArgumentException>(() => new MaterialPrice(1, 1, 1m, date, new string('r', 501), 1, At));
        Assert.Throws<ArgumentException>(() => new Material(1, "A", "x", new string('g', 101), null, 1, At));
        Assert.Equal(90_000.25m, new MaterialPrice(1, 1, 90_000.25m, date, " دلیل ", 1, At).PricePerKgRial);
    }
}
