using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.Tests;

public class ShellViewModelTests
{
    // Fixed UTC+03:30 zone, no daylight saving: independent of the runner's time zone.
    private static readonly TimeZoneInfo SiteZone =
        TimeZoneInfo.CreateCustomTimeZone("test-site", TimeSpan.FromMinutes(210), "test-site", "test-site");

    private static ShellViewModel Create(DateTime utcNow) =>
        new(new JalaliDateService(DigitStyle.Persian, SiteZone), () => utcNow);

    private static readonly DateTime SampleUtc = new(2026, 10, 11, 21, 13, 0, DateTimeKind.Utc);

    [Fact]
    public void Navigation_lists_the_agreed_pages_in_order_with_unique_keys()
    {
        var shell = Create(SampleUtc);

        Assert.Equal(
            new[] { "dashboard", "customers", "requests", "queue", "unitqueue", "workboard", "qcinspection", "jobreports", "overtime", "costing", "completion", "kpi", "calendar", "qcmaster", "machines", "operators", "materials", "contracts", "inquiries", "clarifications", "itemdefinition", "itemimport", "limitations" },
            shell.Items.Select(i => i.Key).ToArray());
        Assert.Equal(shell.Items.Count, shell.Items.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void Dashboard_is_selected_first()
    {
        var shell = Create(SampleUtc);

        Assert.Equal(ShellViewModel.DashboardKey, shell.SelectedItem.Key);
        Assert.Equal(shell.Items[0].Title, shell.CurrentTitle);
    }

    [Fact]
    public void Pages_that_are_not_built_yet_are_marked_as_not_implemented()
    {
        var shell = Create(SampleUtc);

        Assert.True(shell.Items.Single(i => i.Key == ShellViewModel.DashboardKey).IsImplemented);
        Assert.True(shell.Items.Single(i => i.Key == ShellViewModel.LimitationsKey).IsImplemented);
        Assert.True(shell.Items.Single(i => i.Key == ShellViewModel.CustomersKey).IsImplemented);
        Assert.True(shell.Items.Single(i => i.Key == ShellViewModel.RequestsKey).IsImplemented);
        Assert.True(shell.Items.Single(i => i.Key == ShellViewModel.QueueKey).IsImplemented);
    }

    [Fact]
    public void Selecting_a_page_updates_the_title_and_raises_property_changed()
    {
        var shell = Create(SampleUtc);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.Navigate(ShellViewModel.QueueKey);

        Assert.Equal(ShellViewModel.QueueKey, shell.SelectedItem.Key);
        Assert.Equal("صف تولید", shell.CurrentTitle);
        Assert.Contains(nameof(ShellViewModel.SelectedItem), changed);
        Assert.Contains(nameof(ShellViewModel.CurrentTitle), changed);
    }

    [Fact]
    public void Selecting_the_same_page_again_raises_nothing()
    {
        var shell = Create(SampleUtc);
        var count = 0;
        shell.PropertyChanged += (_, _) => count++;

        shell.SelectedItem = shell.SelectedItem;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Unknown_keys_and_foreign_items_are_rejected()
    {
        var shell = Create(SampleUtc);

        Assert.Throws<ArgumentException>(() => shell.Navigate("missing"));
        Assert.Throws<ArgumentException>(() => shell.SelectedItem = new NavigationItem("x", "x", true));
        Assert.Throws<ArgumentNullException>(() => shell.SelectedItem = null!);
    }

    [Fact]
    public void Status_text_is_the_jalali_date_time_in_the_site_zone_with_persian_digits()
    {
        // 2026-10-11 21:13 UTC is 2026-10-12 00:43 at UTC+03:30, which is 1405/07/20.
        var shell = Create(SampleUtc);

        Assert.Equal("۱۴۰۵/۰۷/۲۰ ۰۰:۴۳", shell.StatusText);
    }

    [Fact]
    public void Refreshing_the_clock_follows_the_supplied_time_and_notifies()
    {
        var now = SampleUtc;
        var shell = new ShellViewModel(new JalaliDateService(DigitStyle.Persian, SiteZone), () => now);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        now = now.AddHours(1);
        shell.RefreshClock();

        Assert.Equal("۱۴۰۵/۰۷/۲۰ ۰۱:۴۳", shell.StatusText);
        Assert.Contains(nameof(ShellViewModel.StatusText), changed);
    }

    [Fact]
    public void Dashboard_tiles_show_no_invented_numbers()
    {
        var shell = Create(SampleUtc);

        Assert.Equal(4, shell.DashboardTiles.Count);
        Assert.All(shell.DashboardTiles, t => Assert.Equal("—", t.ValueText));
        Assert.All(shell.DashboardTiles, t => Assert.False(string.IsNullOrWhiteSpace(t.Note)));
    }

    [Fact]
    public void Demonstration_labels_are_present()
    {
        var shell = Create(SampleUtc);

        Assert.False(string.IsNullOrWhiteSpace(shell.DemoBanner));
        Assert.False(string.IsNullOrWhiteSpace(shell.NotImplementedText));
        Assert.Contains("نمایشی", shell.ApplicationTitle);
    }

    [Fact]
    public void Constructor_rejects_missing_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(null!, () => SampleUtc));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(new JalaliDateService(DigitStyle.Persian, SiteZone), null!));
    }

    [Fact]
    public void The_signed_in_user_is_shown_only_once_one_is_set()
    {
        Assert.Equal(string.Empty, Create(SampleUtc).UserText);

        var shell = new ShellViewModel(new JalaliDateService(DigitStyle.Persian, SiteZone), () => SampleUtc) { UserText = "کاربر: نمونه" };

        Assert.Equal("کاربر: نمونه", shell.UserText);
    }
}
