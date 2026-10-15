using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

public class CustomerTests
{
    [Fact]
    public void A_new_customer_is_active_and_keeps_the_name_exactly()
    {
        var customer = new Customer(7, "  Fictional Customer  ", createdByUserId: 3, RequestFixtures.Now);

        Assert.Equal(7, customer.Id);
        Assert.Equal("  Fictional Customer  ", customer.Name);
        Assert.True(customer.IsActive);
        Assert.Equal(3, customer.CreatedByUserId);
        Assert.Equal(RequestFixtures.Now, customer.CreatedAtUtc);
        Assert.Equal(RequestFixtures.Now, customer.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Customer(0, name!, 1, RequestFixtures.Now));
    }

    [Fact]
    public void Timestamps_must_be_utc()
    {
        Assert.Throws<ArgumentException>(() => new Customer(0, "A", 1, new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Renaming_and_deactivating_update_the_audit_time()
    {
        var customer = RequestFixtures.NewCustomer();
        var later = RequestFixtures.Now.AddMinutes(5);

        customer.Rename("Fictional Customer B", later);
        customer.SetActive(false, later.AddMinutes(1));

        Assert.Equal("Fictional Customer B", customer.Name);
        Assert.False(customer.IsActive);
        Assert.Equal(later.AddMinutes(1), customer.UpdatedAtUtc);
        Assert.Throws<ArgumentException>(() => customer.Rename(" ", later));
        Assert.Equal("Fictional Customer B", customer.Name);
    }
}
