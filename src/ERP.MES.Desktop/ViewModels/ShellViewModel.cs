using System.Collections.ObjectModel;
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
    public const string UnitQueueKey = "unitqueue";
    public const string WorkBoardKey = "workboard";
    public const string CalendarKey = "calendar";
    public const string QcInspectionKey = "qcinspection";
    public const string JobReportsKey = "jobreports";
    public const string OvertimeKey = "overtime";
    public const string CostingKey = "costing";
    public const string CompletionKey = "completion";
    public const string KpiKey = "kpi";
    public const string DefinitionsKey = "qcmaster";
    public const string MachinesKey = "machines";
    public const string OperatorsKey = "operators";
    public const string MaterialsKey = "materials";
    public const string ContractsKey = "contracts";
    public const string InquiriesKey = "inquiries";
    public const string ClarificationsKey = "clarifications";
    public const string ItemDefinitionKey = "itemdefinition";
    public const string ItemImportKey = "itemimport";
    public const string UsersKey = "users";
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

        _all =
        [
            new NavigationItem(DashboardKey, "داشبورد", true),
            new NavigationItem(CustomersKey, "مشتریان", true),
            new NavigationItem(RequestsKey, "درخواست‌ها", true),
            new NavigationItem(QueueKey, "صف تولید", true),
            new NavigationItem(UnitQueueKey, "صف واحدهای تولید", true),
            new NavigationItem(WorkBoardKey, "نوبت‌های کاری", true),
            new NavigationItem(QcInspectionKey, "بازرسی کنترل کیفیت", true),
            new NavigationItem(JobReportsKey, "گزارش کار روزانه", true),
            new NavigationItem(OvertimeKey, "اضافه‌کاری", true),
            new NavigationItem(CostingKey, "هزینه و قیمت", true),
            new NavigationItem(CompletionKey, "دریافت انبار و تکمیل", true),
            new NavigationItem(KpiKey, "شاخص‌های واحدها", true),
            new NavigationItem(CalendarKey, "تقویم و شیفت‌ها", true),
            new NavigationItem(DefinitionsKey, "واحدها و انواع مقدار", true),
            new NavigationItem(MachinesKey, "ماشین‌ها و نرخ‌ها", true),
            new NavigationItem(OperatorsKey, "اپراتورها", true),
            new NavigationItem(MaterialsKey, "مواد اولیه و قیمت", true),
            new NavigationItem(ContractsKey, "قراردادها", true),
            new NavigationItem(InquiriesKey, "استعلام مواد اولیه", true),
            new NavigationItem(ClarificationsKey, "پرسش‌های نقشه‌کشی", true),
            new NavigationItem(ItemDefinitionKey, "تعریف آیتم‌ها و مرحله‌ها", true),
            new NavigationItem(ItemImportKey, "ورود آیتم‌ها از اکسل", true),
            new NavigationItem(UsersKey, "کاربران و حساب‌ها", true),
            new NavigationItem(LimitationsKey, "محدودیت‌های نسخه نمایشی", true),
        ];

        Items = new ObservableCollection<NavigationItem>(_all);

        DashboardTiles =
        [
            new DashboardTile("درخواست‌های باز", "—", "هنوز داده‌ای بارگذاری نشده است"),
            new DashboardTile("آیتم‌های در تولید", "—", "هنوز داده‌ای بارگذاری نشده است"),
            new DashboardTile("در انتظار تأیید کنترل کیفیت", "—", "هنوز داده‌ای بارگذاری نشده است"),
            new DashboardTile("آیتم‌های فوری", "—", "هنوز داده‌ای بارگذاری نشده است"),
        ];

        _selectedItem = _all[0];
        _statusText = BuildStatusText();
    }

    public string ApplicationTitle => "سامانه ERP/MES — نسخه نمایشی";

    public string DemoBanner => "نسخه نمایشی: فقط داده‌های فرضی؛ بخش‌های ناقص با برچسب مشخص شده‌اند و کامل‌شده معرفی نمی‌شوند.";

    public string NotImplementedText => "این بخش در این ساخت هنوز پیاده‌سازی نشده است.";

    private readonly IReadOnlyList<NavigationItem> _all;

    /// <summary>The pages shown in the navigation list. All of them until <see cref="ApplyAccess"/> narrows the list to the user's permissions.</summary>
    public IReadOnlyList<NavigationItem> Items { get; }

    /// <summary>
    /// Shows only the pages whose key is in <paramref name="visibleKeys"/> (see <see cref="PageAccess"/>). The selected page falls back to the dashboard when the user may not see it. This only
    /// shapes the menu; it authorizes nothing.
    /// </summary>
    public void ApplyAccess(IReadOnlySet<string> visibleKeys)
    {
        ArgumentNullException.ThrowIfNull(visibleKeys);
        var list = (ObservableCollection<NavigationItem>)Items;
        var wanted = _all.Where(i => i.Key == DashboardKey || visibleKeys.Contains(i.Key)).ToList();

        // Leave a page the user may not see BEFORE it is removed from the list, so the list box never has to report "nothing selected".
        if (wanted.All(i => i.Key != _selectedItem.Key))
        {
            SelectedItem = list.First(i => i.Key == DashboardKey);
        }

        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (wanted.All(w => w.Key != list[index].Key))
            {
                list.RemoveAt(index);
            }
        }

        for (var position = 0; position < wanted.Count; position++)
        {
            if (position >= list.Count || list[position].Key != wanted[position].Key)
            {
                list.Insert(position, wanted[position]);
            }
        }
    }

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
