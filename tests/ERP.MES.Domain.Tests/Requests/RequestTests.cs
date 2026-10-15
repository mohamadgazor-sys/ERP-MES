using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

public class RequestTests
{
    [Fact]
    public void A_new_request_starts_as_created_and_keeps_the_customer_supplied_number()
    {
        var request = RequestFixtures.NewRequest("wsj-898", customerId: 5);

        Assert.Equal(RequestStatus.Created, request.Status);
        Assert.Equal("CREATED", request.Status.Code());
        Assert.Equal("wsj-898", request.RequestNo.Value);
        Assert.Equal(5, request.CustomerId);
        Assert.Empty(request.Items);
        Assert.Equal(RequestFixtures.Now, request.CreatedAtUtc);
    }

    [Fact]
    public void The_status_set_is_the_approved_eight()
    {
        var codes = Enum.GetValues<RequestStatus>().Select(s => s.Code()).ToArray();

        Assert.Equal(
            new[]
            {
                "CREATED", "SENT_TO_MANAGEMENT", "MGMT_APPROVED_AT_PLANNING", "MGMT_REJECTED_AT_PLANNING",
                "UNDER_DRAWING_REVIEW", "DRAWING_RETURNED_TO_PLANNING", "CANCELLED", "CLOSED",
            },
            codes);
    }

    [Theory]
    [InlineData(2026, 3, 20, 1404)] // day before Nowruz 1405
    [InlineData(2026, 3, 21, 1405)] // 1 Farvardin 1405
    [InlineData(2026, 10, 12, 1405)]
    [InlineData(2027, 3, 20, 1405)] // last day of Esfand 1405
    [InlineData(2027, 3, 21, 1406)]
    public void The_jalali_request_year_is_derived_from_the_request_date_at_creation(int year, int month, int day, int expectedYear)
    {
        var request = RequestFixtures.NewRequest(requestDate: new DateOnly(year, month, day));

        Assert.Equal(expectedYear, request.JalaliRequestYear);
        Assert.Equal(new DateOnly(year, month, day), request.RequestDate);
    }

    [Fact]
    public void The_business_key_is_customer_request_number_and_jalali_year()
    {
        var a = RequestFixtures.NewRequest("P-565", customerId: 1);

        Assert.Equal(new RequestKey(1, "P-565", 1405), a.Key);
    }

    [Fact]
    public void The_same_number_is_a_different_key_for_another_customer_or_another_jalali_year()
    {
        var baseline = RequestFixtures.NewRequest("27", customerId: 1, requestDate: new DateOnly(2026, 10, 12));
        var otherCustomer = RequestFixtures.NewRequest("27", customerId: 2, requestDate: new DateOnly(2026, 10, 12));
        var nextYear = RequestFixtures.NewRequest("27", customerId: 1, requestDate: new DateOnly(2027, 3, 21));

        Assert.NotEqual(baseline.Key, otherCustomer.Key);
        Assert.NotEqual(baseline.Key, nextYear.Key);
    }

    [Fact]
    public void The_key_compares_the_number_case_sensitively()
    {
        var lower = RequestFixtures.NewRequest("wsj-898");
        var upper = RequestFixtures.NewRequest("WSJ-898");
        var same = RequestFixtures.NewRequest("wsj-898");

        Assert.NotEqual(lower.Key, upper.Key);
        Assert.Equal(lower.Key, same.Key);
    }

    [Fact]
    public void A_request_may_mix_make_and_repair_items()
    {
        var request = RequestFixtures.NewRequest();

        var make = request.AddMake("1.10", 10);
        var repair = request.AddRepair("2.10", 4);

        Assert.Equal(new[] { WorkType.Make, WorkType.Repair }, request.Items.Select(i => i.WorkType).ToArray());
        Assert.Equal(new[] { "MAKE", "REPAIR" }, request.Items.Select(i => i.WorkType.Code()).ToArray());
        Assert.Same(make, request.Items[0]);
        Assert.Same(repair, request.Items[1]);
    }

    [Fact]
    public void Item_numbers_are_unique_within_the_request_and_compared_exactly()
    {
        var request = RequestFixtures.NewRequest();
        request.AddMake("1.10");

        Assert.True(request.HasItemNo("1.10"));
        Assert.Throws<InvalidOperationException>(() => request.AddRepair("1.10"));
        Assert.Single(request.Items);

        // Different text is a different item number: no truncation, no trimming, no case folding.
        Assert.False(request.HasItemNo("1.1"));
        Assert.False(request.HasItemNo("1.10 "));
        request.AddMake("1.1");
        request.AddMake("A-01");
        request.AddMake("a-01");
        Assert.Equal(4, request.Items.Count);
    }

    [Fact]
    public void Item_numbers_are_never_parsed_numerically()
    {
        var request = RequestFixtures.NewRequest();
        request.AddMake("1");
        request.AddMake("01");
        request.AddMake("1.0");
        request.AddMake("1.00");

        Assert.Equal(new[] { "1", "01", "1.0", "1.00" }, request.Items.Select(i => i.ItemNo).ToArray());
    }

    [Fact]
    public void Exact_decimal_quantities_are_kept_without_rounding()
    {
        var item = RequestFixtures.NewRequest().AddMake("1.10", quantity: 10.123456789m);

        Assert.Equal(10.123456789m, item.RequestedQuantity);
    }

    [Fact]
    public void New_items_default_to_normal_priority_and_planning_may_raise_it()
    {
        var request = RequestFixtures.NewRequest();

        var defaulted = request.AddItem(0, "1.10", "Ring", WorkType.Make, 1, 1, RequestFixtures.Now);
        var explicitUrgent = request.AddItem(0, "1.20", "Ring", WorkType.Make, 1, 1, RequestFixtures.Now, Priority.Urgent);

        Assert.Equal(Priority.Normal, defaulted.Priority);
        Assert.Equal(Priority.Urgent, explicitUrgent.Priority);
        Assert.True(defaulted.ChangePriority(Priority.VeryUrgent, RequestFixtures.Now.AddMinutes(1)));
        Assert.Equal(Priority.VeryUrgent, defaulted.Priority);
    }

    [Theory]
    [InlineData(RequestStatus.Cancelled)]
    [InlineData(RequestStatus.Closed)]
    public void Cancelled_and_closed_requests_accept_no_new_items(RequestStatus status)
    {
        var request = RequestFixtures.NewRequest();
        request.AddMake("1.10");
        request.SetStatus(status);

        Assert.Throws<InvalidOperationException>(() => request.AddMake("1.20"));
        Assert.Single(request.Items);
    }

    [Theory]
    [InlineData(RequestStatus.SentToManagement)]
    [InlineData(RequestStatus.MgmtApprovedAtPlanning)]
    [InlineData(RequestStatus.Closed)]
    public void A_request_can_be_placed_directly_in_an_approved_status_for_the_demonstration(RequestStatus status)
    {
        var request = RequestFixtures.NewRequest();
        var changedAt = RequestFixtures.Now.AddHours(3);

        request.PlaceInStatusForDemonstration(status, changedAt);

        Assert.Equal(status, request.Status);
        Assert.Equal(changedAt, request.UpdatedAtUtc);
    }

    [Fact]
    public void Placing_a_request_in_a_status_rejects_unknown_statuses_and_non_utc_times()
    {
        var request = RequestFixtures.NewRequest();

        Assert.Throws<ArgumentOutOfRangeException>(() => request.PlaceInStatusForDemonstration((RequestStatus)99, RequestFixtures.Now));
        Assert.Throws<ArgumentException>(() => request.PlaceInStatusForDemonstration(RequestStatus.Closed, DateTime.SpecifyKind(RequestFixtures.Now, DateTimeKind.Unspecified)));
        Assert.Equal(RequestStatus.Created, request.Status);
    }

    [Fact]
    public void The_demonstration_status_placement_is_internal_and_the_request_has_no_public_status_setter()
    {
        const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        var placement = typeof(Request).GetMethod("PlaceInStatusForDemonstration", instance);
        Assert.NotNull(placement);
        Assert.False(placement.IsPublic);
        Assert.True(placement.IsAssembly);

        // No public member can change the status: it has a private setter and no public method is named like a status change.
        Assert.False(typeof(Request).GetProperty(nameof(Request.Status))!.SetMethod!.IsPublic);
        var publicNames = typeof(Request).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();
        Assert.DoesNotContain(publicNames, n => n.Contains("Status", StringComparison.Ordinal));
    }

    [Fact]
    public void A_newly_created_request_accepts_items()
    {
        var request = RequestFixtures.NewRequest();

        Assert.Equal(RequestStatus.Created, request.Status);
        request.AddMake("1.10");
        Assert.Single(request.Items);
    }

    [Fact]
    public void The_same_item_number_may_exist_in_different_requests()
    {
        var first = RequestFixtures.NewRequest("1", requestId: 1);
        var second = RequestFixtures.NewRequest("2", requestId: 2);

        first.AddMake("1.10");
        second.AddMake("1.10");

        Assert.Single(first.Items);
        Assert.Single(second.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_item_number_is_required(string? itemNo)
    {
        var request = RequestFixtures.NewRequest();

        Assert.Throws<ArgumentException>(() => request.AddMake(itemNo!));
        Assert.Empty(request.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_requested_quantity_must_be_positive(int quantity)
    {
        var request = RequestFixtures.NewRequest();

        Assert.Throws<ArgumentOutOfRangeException>(() => request.AddMake("1.10", quantity));
        Assert.Empty(request.Items);
    }

    [Fact]
    public void Item_data_is_validated()
    {
        var request = RequestFixtures.NewRequest();

        Assert.Throws<ArgumentException>(() => request.AddItem(0, "1.10", " ", WorkType.Make, 1, 1, RequestFixtures.Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => request.AddItem(0, "1.10", "Ring", (WorkType)9, 1, 1, RequestFixtures.Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => request.AddItem(0, "1.10", "Ring", WorkType.Make, 1, 1, RequestFixtures.Now, (Priority)9));
        Assert.Throws<ArgumentException>(() => request.AddItem(0, "1.10", "Ring", WorkType.Make, 1, 1, new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Local)));
        Assert.Empty(request.Items);
    }

    [Fact]
    public void Creation_rejects_missing_arguments_and_non_utc_times()
    {
        var customer = RequestFixtures.NewCustomer();
        var number = RequestNo.Create("1");
        var date = new DateOnly(2026, 10, 12);

        Assert.Throws<ArgumentNullException>(() => Request.Create(1, null!, number, date, null, null, 1, RequestFixtures.Now, RequestFixtures.Dates));
        Assert.Throws<ArgumentNullException>(() => Request.Create(1, customer, null!, date, null, null, 1, RequestFixtures.Now, RequestFixtures.Dates));
        Assert.Throws<ArgumentNullException>(() => Request.Create(1, customer, number, date, null, null, 1, RequestFixtures.Now, null!));
        Assert.Throws<ArgumentException>(() => Request.Create(1, customer, number, date, null, null, 1, DateTime.SpecifyKind(RequestFixtures.Now, DateTimeKind.Local), RequestFixtures.Dates));
    }
}
