using ERP.MES.Application.Security;

namespace ERP.MES.Desktop.ViewModels;

public enum SignInMode
{
    /// <summary>Reading the setup state.</summary>
    Checking,

    /// <summary>The system has no administrator and no pending token: no input is offered (Milestone 1 plan 5.2).</summary>
    NotProvisioned,

    /// <summary>A bootstrap token is pending: token, new administrator name and password.</summary>
    FirstRun,

    SignIn,

    /// <summary>A recovery token is pending and the person chose to use it.</summary>
    Recovery,

    /// <summary>The user signed in with a one-time password and has to choose their own before continuing.</summary>
    ChangePassword,

    Done,
}

/// <summary>
/// State and actions of the sign-in window. It talks only to the Application use cases; it never touches a password beyond handing it to them
/// (the window clears its password boxes after each action), never stores one, and shows generic messages that do not reveal whether a user name exists.
/// </summary>
public sealed class SignInViewModel(
    AuthenticationService authentication,
    BootstrapService bootstrap,
    RecoveryService recovery,
    AccountService accounts,
    int minimumPasswordLength) : ObservableObject
{
    private SignInMode _mode = SignInMode.Checking;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isRecoveryAvailable;
    private Principal? _pendingUser;

    /// <summary>Raised once, when the user is fully signed in (and has no pending password change).</summary>
    public event EventHandler<Principal>? SignedIn;

    public string Title => "ورود به سامانه ERP/MES — نسخه نمایشی";

    public SignInMode Mode
    {
        get => _mode;
        private set => SetProperty(ref _mode, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>True only while a valid recovery token is pending: the "use recovery token" option is offered only then.</summary>
    public bool IsRecoveryAvailable
    {
        get => _isRecoveryAvailable;
        private set => SetProperty(ref _isRecoveryAvailable, value);
    }

    /// <summary>Reads the setup state and chooses the screen: not initialised, first run (bootstrap), or normal sign-in.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            Message = string.Empty;
            switch (await bootstrap.GetStateAsync(cancellationToken))
            {
                case SetupState.NotProvisioned:
                    Mode = SignInMode.NotProvisioned;
                    Message = "سامانه هنوز راه‌اندازی نشده است. با مدیر سیستم تماس بگیرید.";
                    break;
                case SetupState.AwaitingBootstrap:
                    Mode = SignInMode.FirstRun;
                    break;
                default:
                    IsRecoveryAvailable = await recovery.IsRecoveryPendingAsync(cancellationToken);
                    Mode = SignInMode.SignIn;
                    break;
            }
        });
    }

    public async Task SignInAsync(string? username, string? password, CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            var result = await authentication.SignInAsync(username, password, cancellationToken);
            switch (result.Status)
            {
                case SignInStatus.Succeeded:
                    Message = string.Empty;
                    if (result.Principal!.MustChangePassword)
                    {
                        _pendingUser = result.Principal;
                        Mode = SignInMode.ChangePassword;
                        Message = "این گذرواژه یک‌بار مصرف است. لطفاً گذرواژه دلخواه خود را انتخاب کنید.";
                    }
                    else
                    {
                        Complete(result.Principal);
                    }

                    break;
                default:
                    // One message for every failure, including a locked account (Decision Log #65): the caller learns nothing about which it was.
                    Message = "نام کاربری یا گذرواژه نادرست است.";
                    break;
            }
        });
    }

    public async Task CompleteFirstRunAsync(
        string? token, string? username, string? displayName, string? password, string? repeatedPassword, CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            if (password != repeatedPassword)
            {
                Message = "تکرار گذرواژه با گذرواژه یکسان نیست.";
                return;
            }

            var result = await bootstrap.CompleteAsync(token, username, displayName, password, cancellationToken);
            switch (result.Status)
            {
                case BootstrapStatus.Succeeded:
                    // The first administrator exists; from now on only the normal sign-in is offered.
                    IsRecoveryAvailable = false;
                    Mode = SignInMode.SignIn;
                    Message = "مدیر سیستم ایجاد شد. اکنون وارد شوید.";
                    break;
                case BootstrapStatus.AlreadyInitialized:
                    IsRecoveryAvailable = await recovery.IsRecoveryPendingAsync(cancellationToken);
                    Mode = SignInMode.SignIn;
                    Message = "سامانه قبلاً راه‌اندازی شده است.";
                    break;
                case BootstrapStatus.NotAwaitingBootstrap:
                    Mode = SignInMode.NotProvisioned;
                    Message = "سامانه هنوز راه‌اندازی نشده است. با مدیر سیستم تماس بگیرید.";
                    break;
                case BootstrapStatus.TokenExpiredOrLocked:
                    Mode = SignInMode.NotProvisioned;
                    Message = "توکن منقضی شده یا تلاش‌های مجاز به پایان رسیده است. از مدیر سیستم توکن جدید بخواهید.";
                    break;
                case BootstrapStatus.TokenInvalid:
                    Message = "توکن نادرست است.";
                    break;
                case BootstrapStatus.UsernameRejected:
                    Message = "نام کاربری معتبر نیست (خالی، بیش‌ازحد بلند یا دارای فاصله/نویسه کنترلی).";
                    break;
                case BootstrapStatus.DisplayNameRejected:
                    Message = "نام نمایشی را وارد کنید.";
                    break;
                default:
                    Message = PasswordMessage(result.PasswordError);
                    break;
            }
        });
    }

    public void ShowRecovery()
    {
        if (Mode == SignInMode.SignIn && IsRecoveryAvailable)
        {
            Message = string.Empty;
            Mode = SignInMode.Recovery;
        }
    }

    public void BackToSignIn()
    {
        if (Mode == SignInMode.Recovery)
        {
            Message = string.Empty;
            Mode = SignInMode.SignIn;
        }
    }

    public async Task RedeemRecoveryAsync(string? token, string? newPassword, string? repeatedPassword, CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            if (newPassword != repeatedPassword)
            {
                Message = "تکرار گذرواژه با گذرواژه یکسان نیست.";
                return;
            }

            var result = await recovery.RedeemAsync(token, newPassword, cancellationToken);
            switch (result.Status)
            {
                case RecoveryStatus.Succeeded:
                    IsRecoveryAvailable = false;
                    Mode = SignInMode.SignIn;
                    Message = "گذرواژه جدید ثبت شد. اکنون با آن وارد شوید.";
                    break;
                case RecoveryStatus.TokenInvalid:
                    Message = "توکن نادرست است.";
                    break;
                case RecoveryStatus.PasswordUnchanged:
                    Message = "گذرواژه جدید باید با گذرواژه قبلی متفاوت باشد.";
                    break;
                case RecoveryStatus.PasswordRejected:
                    Message = PasswordMessage(result.PasswordError);
                    break;
                default:
                    IsRecoveryAvailable = await recovery.IsRecoveryPendingAsync(cancellationToken);
                    Mode = SignInMode.SignIn;
                    Message = "توکن بازیابی منقضی شده یا دیگر معتبر نیست. در صورت نیاز از مدیر سیستم توکن جدید بخواهید.";
                    break;
            }
        });
    }

    public async Task ChangePasswordAsync(string? currentPassword, string? newPassword, string? repeatedPassword, CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            if (_pendingUser is null)
            {
                return;
            }

            if (newPassword != repeatedPassword)
            {
                Message = "تکرار گذرواژه با گذرواژه یکسان نیست.";
                return;
            }

            var result = await accounts.ChangeOwnPasswordAsync(_pendingUser, currentPassword, newPassword, cancellationToken);
            switch (result.Status)
            {
                case AccountStatus.Succeeded:
                    var user = _pendingUser with { MustChangePassword = false };
                    _pendingUser = null;
                    Complete(user);
                    break;
                case AccountStatus.WrongCurrentPassword:
                    Message = "گذرواژه فعلی نادرست است.";
                    break;
                case AccountStatus.PasswordUnchanged:
                    Message = "گذرواژه جدید باید با گذرواژه فعلی متفاوت باشد.";
                    break;
                case AccountStatus.PasswordRejected:
                    Message = PasswordMessage(result.PasswordError);
                    break;
                default:
                    _pendingUser = null;
                    Mode = SignInMode.SignIn;
                    Message = "امکان تغییر گذرواژه نبود. دوباره وارد شوید.";
                    break;
            }
        });
    }

    /// <summary>A neutral notice on the sign-in screen, for example after a session ended. It never says why more than a normal failed sign-in would.</summary>
    public void ShowNotice(string message)
    {
        if (Mode == SignInMode.SignIn)
        {
            Message = message;
        }
    }

    /// <summary>Shown when something unexpected failed (for example the database was unreachable). No technical detail or secret is shown.</summary>
    public void ReportUnexpectedFailure() =>
        Message = "خطای غیرمنتظره رخ داد. دوباره تلاش کنید؛ اگر ادامه یافت با مدیر سیستم تماس بگیرید.";

    private void Complete(Principal user)
    {
        Mode = SignInMode.Done;
        Message = string.Empty;
        SignedIn?.Invoke(this, user);
    }

    private string PasswordMessage(PasswordPolicyError error) => error switch
    {
        PasswordPolicyError.TooShort => $"گذرواژه باید دست‌کم {minimumPasswordLength} نویسه باشد.",
        PasswordPolicyError.ContainsWhitespace => "گذرواژه نباید شامل فاصله، تب یا خط جدید (هیچ نویسهٔ خالی) باشد.",
        _ => "گذرواژه را وارد کنید.",
    };

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
