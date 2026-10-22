using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// State of the main window of the demonstration build: the navigation list, the selected page, the demonstration
/// banner and the Jalali date and time shown in the status bar. All date text comes from <see cref="IJalaliDateService"/>.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    public const string DashboardKey = "dashboard";
    public const string CustomersKey = "customers";
    public const string RequestsKey = "requests";
    public const string QueueKey = "queue";
    public const string StagesKey = "stages";
    public const string DefinitionsKey = "qcmaster";
    public const string MachinesKey = "machines";
    public const string LimitationsKey = "limitations";

    private readonly IJalaliDateService _dates;
    private readonly Func<DateTime> _utcNow;
    private NavigationItem _selectedItem;
    private string _statusText;
    private string _userText = string.Empty;

    public ShellViewModel(IJalaliDateService dates, Func<DateTime> utcNow)
    {
        _dates = dates ?? throw new ArgumentNullException(nameof(dates));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));

        Items =
        [
            new NavigationItem(DashboardKey, "داشبورد", true),
            new NavigationItem(CustomersKey, "مشتریان", true),
            new NavigationItem(RequestsKey, "درخواست‌ها", true),
            new NavigationItem(QueueKey, "صف تولید", true),
            new NavigationItem(StagesKey, "مراحل و کنترل کیفیت", true),
            new NavigationItem(DefinitionsKey, "واحدها و انواع مقدار", true),
            new NavigationItem(MachinesKey, "ماشین‌ها و نرخ‌ها", true),
            new NavigationItem(LimitationsKey, "محدودیت‌های نسخه نمایشی", true),
        ];

        DashboardTiles =
        [
            new DashboardTile("درخواست‌های باز", "—", "هنوز داده‌ای بارگذاری نشده است"),
            new DashboardTile("آیتم‌های در تولید", "—", "هنوز داده‌ای بارگذاری نشده است"),
            new DashboardTile("در انتظار تأیید کنترل کیفیت", "—", "هنوز داده‌ای بارگذاری نشده است"),
            new DashboardTile("آیتم‌های فوری", "—", "هنوز داده‌ای بارگذاری نشده است"),
        ];

        _selectedItem = Items[0];
        _statusText = BuildStatusText();
    }

    public string ApplicationTitle => "سامانه ERP/MES — نسخه نمایشی";

    public string DemoBanner => "نسخه نمایشی: فقط داده‌های فرضی؛ بخش‌های ناقص با برچسب مشخص شده‌اند و کامل‌شده معرفی نمی‌شوند.";

    public string NotImplementedText => "این بخش در این ساخت هنوز پیاده‌سازی نشده است.";

    public IReadOnlyList<NavigationItem> Items { get; }

    public IReadOnlyList<DashboardTile> DashboardTiles { get; }

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!Items.Contains(value))
            {
                throw new ArgumentException("The item is not part of the navigation list.", nameof(value));
            }

            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(CurrentTitle));
            }
        }
    }

    public string CurrentTitle => _selectedItem.Title;

    /// <summary>Jalali date and time (Persian digits, 24-hour clock) in the site time zone, from <see cref="IJalaliDateService"/>.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The signed-in user, as shown in the status bar. Empty until someone has signed in.</summary>
    public string UserText
    {
        get => _userText;
        init => _userText = value;
    }

    public void RefreshClock() => StatusText = BuildStatusText();

    public void Navigate(string key)
    {
        var item = Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal))
            ?? throw new ArgumentException("Unknown page key: " + key, nameof(key));
        SelectedItem = item;
    }

    private string BuildStatusText() => _dates.FormatDateTime(_utcNow());
}
