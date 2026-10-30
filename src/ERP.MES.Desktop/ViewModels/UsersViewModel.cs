using System.Collections.ObjectModel;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One account row: identity, department and role (by stable code, shown with their Persian names), the inactive marker, last sign-in and the working units.</summary>
public sealed record UserRow(long UserId, string Username, string DisplayName, string DepartmentCode, string RoleCode, bool IsActive, string LastLoginText, string WorkingUnitsText)
{
    public string DepartmentText => Messages.DepartmentText(DepartmentCode);

    public string RoleText => UsersViewModel.RoleName(RoleCode);

    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

/// <summary>
/// The user and account administration screen (Decision Log #122): Program Admin creates accounts with a one-time password, assigns the department (organizational unit) and role, deactivates, resets
/// passwords and assigns the operational working units. It relays typed values to <see cref="AccountService"/> and <see cref="UnitDesignationService"/>; they check the Admin role again on every call.
/// The working unit is operational data, separate from the department and role that decide permissions.
/// </summary>
public sealed class UsersViewModel(
    AccountService accounts, UserDirectoryService directory, UnitDesignationService designations, PlanningMasterService planning, IJalaliDateService dates, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<UserRow> _users = [];
    private IReadOnlyList<UnitInfo> _units = [];
    private IReadOnlyList<DesignationRow> _designations = [];
    private UserRow? _selected;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private bool _isDenied;
    private bool _isError;

    public ObservableCollection<UserRow> Users => _users;

    public IReadOnlyList<Choice<string>> DepartmentChoices { get; } = DepartmentCodes.All.Select(c => new Choice<string>(c, Messages.DepartmentText(c))).ToList();

    public IReadOnlyList<Choice<string>> RoleChoices { get; } = RoleCodes.All.Select(c => new Choice<string>(c, RoleName(c))).ToList();

    public IReadOnlyList<UnitInfo> Units => _units;

    public UserRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsErrorMessage
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    private void Succeed(string text)
    {
        IsErrorMessage = false;
        Message = text;
    }

    private void Fail(string text)
    {
        IsErrorMessage = true;
        Message = text;
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>True when the signed-in user may not administer accounts (the list is then empty).</summary>
    public bool IsDenied
    {
        get => _isDenied;
        private set => SetProperty(ref _isDenied, value);
    }

    public static string RoleName(string code) => code switch
    {
        RoleCodes.Admin => "مدیر سیستم",
        RoleCodes.Manager => "مدیر",
        RoleCodes.Staff => "کارمند",
        _ => code,
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(() => ReloadAsync(cancellationToken, keepMessage: false), showLoading: true);

    public async Task<bool> CreateAsync(string? username, string? displayName, string? departmentCode, string? roleCode, string? initialPassword, CancellationToken cancellationToken = default) =>
        departmentCode is null || roleCode is null
            ? Nothing()
            : await ChangeAsync(() => accounts.CreateUserAsync(currentUser(), username, displayName, departmentCode, roleCode, initialPassword, cancellationToken), "حساب ایجاد شد؛ کاربر در نخستین ورود گذرواژهٔ خود را انتخاب می‌کند.", cancellationToken);

    public async Task<bool> ChangeDepartmentAsync(string? departmentCode, CancellationToken cancellationToken = default) =>
        _selected is { } user && departmentCode is not null
            ? await ChangeAsync(() => accounts.ChangeDepartmentAsync(currentUser(), user.UserId, departmentCode, cancellationToken), "واحد سازمانی کاربر تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> ChangeRoleAsync(string? roleCode, CancellationToken cancellationToken = default) =>
        _selected is { } user && roleCode is not null
            ? await ChangeAsync(() => accounts.ChangeRoleAsync(currentUser(), user.UserId, roleCode, cancellationToken), "نقش کاربر تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selected is { } user
            ? await ChangeAsync(() => accounts.SetActiveAsync(currentUser(), user.UserId, isActive, cancellationToken), isActive ? "حساب فعال شد." : "حساب غیرفعال شد.", cancellationToken)
            : Nothing();

    public async Task<bool> ResetPasswordAsync(string? newPassword, CancellationToken cancellationToken = default) =>
        _selected is { } user
            ? await ChangeAsync(() => accounts.ResetPasswordAsync(currentUser(), user.UserId, newPassword, cancellationToken), "گذرواژهٔ یکبارمصرف تعیین شد؛ کاربر در ورود بعدی گذرواژهٔ خود را انتخاب می‌کند.", cancellationToken)
            : Nothing();

    public async Task<bool> UnlockAsync(CancellationToken cancellationToken = default) =>
        _selected is { } user
            ? await ChangeAsync(() => accounts.UnlockAsync(currentUser(), user.UserId, cancellationToken), "قفل حساب باز شد.", cancellationToken)
            : Nothing();

    /// <summary>Assigns the selected user's working unit (an operational assignment of a Production user, not a permission).</summary>
    public async Task<bool> AssignWorkingUnitAsync(long unitId, CancellationToken cancellationToken = default)
    {
        if (_selected is not { } user)
        {
            return Nothing();
        }

        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await designations.AssignAsync(currentUser(), user.UserId, unitId, cancellationToken);
            if (!outcome.Succeeded)
            {
                Fail(Messages.For(outcome));
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            Succeed("واحد کاری کاربر ثبت شد.");
        });
        return saved;
    }

    /// <summary>Withdraws the selected user's active designation to the given unit.</summary>
    public async Task<bool> RevokeWorkingUnitAsync(long unitId, CancellationToken cancellationToken = default)
    {
        if (_selected is not { } user || _designations.FirstOrDefault(d => d.UserId == user.UserId && d.UnitId == unitId && d.IsActive) is not { } row)
        {
            return Nothing();
        }

        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await designations.RevokeAsync(currentUser(), row.DesignationId, row.Version, cancellationToken);
            if (!outcome.Succeeded)
            {
                Fail(Messages.For(outcome));
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            Succeed("واحد کاری کاربر برداشته شد.");
        });
        return saved;
    }

    private bool Nothing()
    {
        Fail(Messages.NothingSelected);
        return false;
    }

    private async Task<bool> ChangeAsync(Func<Task<AccountResult>> call, string success, CancellationToken cancellationToken)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var result = await call();
            if (result.Status != AccountStatus.Succeeded)
            {
                Fail(AccountMessage(result));
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            Succeed(success);
        });
        return saved;
    }

    private static string AccountMessage(AccountResult result) => result.Status switch
    {
        AccountStatus.NotAuthorized => Messages.NotAuthorized,
        AccountStatus.NotFound => "این کاربر دیگر وجود ندارد؛ فهرست را دوباره بارگذاری کنید.",
        AccountStatus.UsernameTaken => "این نام کاربری قبلاً استفاده شده است (بزرگی و کوچکی حروف تفاوتی ندارد).",
        AccountStatus.UsernameRejected => result.UsernameError switch
        {
            UsernameError.Empty => "نام کاربری را وارد کنید.",
            UsernameError.TooLong => "نام کاربری نباید از ۶۴ نویسه بیشتر باشد.",
            UsernameError.LeadingOrTrailingWhitespace => "نام کاربری نباید در ابتدا یا انتها فاصله داشته باشد.",
            _ => "نام کاربری نویسهٔ غیرمجاز دارد.",
        },
        AccountStatus.DisplayNameRejected => "نام نمایشی را وارد کنید.",
        AccountStatus.PasswordRejected => result.PasswordError switch
        {
            PasswordPolicyError.TooShort => "گذرواژه کوتاه است (دست‌کم ۶ نویسه).",
            PasswordPolicyError.ContainsWhitespace => "گذرواژه نباید شامل فاصله، تب یا خط جدید باشد.",
            _ => "گذرواژه را وارد کنید.",
        },
        AccountStatus.InvalidDepartmentOrRole => "واحد سازمانی یا نقش نامعتبر است.",
        AccountStatus.LastAdministrator => "آخرین مدیر سیستم فعال را نمی‌توان غیرفعال یا تنزل داد.",
        _ => Messages.Unexpected,
    };

    private async Task ReloadAsync(CancellationToken cancellationToken, bool keepMessage)
    {
        if (!keepMessage)
        {
            IsErrorMessage = false;
            Message = string.Empty;
        }

        var listed = await directory.ListAsync(currentUser(), cancellationToken);
        if (!listed.Succeeded)
        {
            _users.Clear();
            IsDenied = listed.Status == OperationStatus.NotAuthorized;
            Fail(Messages.For(listed));
            return;
        }

        IsDenied = false;
        var keep = _selected?.UserId;
        var overview = await designations.LoadAsync(currentUser(), cancellationToken);
        _designations = overview.Succeeded ? overview.Value!.Rows : [];
        _units = overview.Succeeded ? overview.Value!.Units.Where(u => u.IsActive).ToList() : [];
        _users.Clear();
        foreach (var user in listed.Value!)
        {
            var units = string.Join("، ", _designations.Where(d => d.UserId == user.UserId && d.IsActive).Select(d => d.UnitCode));
            _users.Add(new UserRow(
                user.UserId, user.Username, user.DisplayName, user.DepartmentCode, user.RoleCode, user.IsActive,
                user.LastLoginAtUtc is { } at ? dates.FormatDateTime(at) : "—", units.Length == 0 ? "—" : units));
        }

        _selected = _users.FirstOrDefault(u => u.UserId == keep);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Units));
    }

    private async Task RunAsync(Func<Task> action, bool showLoading = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsLoading = showLoading;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // The screen was closed; nothing to report.
        }
        catch (Exception)
        {
            Fail(Messages.Unexpected);
        }
        finally
        {
            IsLoading = false;
            IsBusy = false;
        }
    }
}
