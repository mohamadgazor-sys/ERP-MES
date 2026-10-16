using ERP.MES.Application.Security;
using Microsoft.Win32;

namespace ERP.MES.Desktop.Session;

public enum SessionState
{
    Active,
    Locked,
    Ended,
}

/// <summary>How a session ended, so the application knows what to show next.</summary>
public enum SessionEndReason
{
    /// <summary>The user signed out; the sign-in window is shown.</summary>
    SignedOut,

    /// <summary>Another user chose to sign in from the lock screen; the sign-in window is shown.</summary>
    SwitchedUser,

    /// <summary>The account was deactivated or locked out while the session was locked; the sign-in window is shown.</summary>
    AccountUnavailable,

    /// <summary>The application is closing; nothing is shown.</summary>
    ApplicationClosed,
}

/// <summary>
/// One signed-in desktop session (Decision Log #74). It lives in memory only: there is no session token, refresh token or stored login, so closing the application
/// always ends it and the next launch always needs a sign-in. The <see cref="Principal"/> is a UI snapshot - after an unlock it is replaced by the user as freshly
/// loaded from the database - and never authorizes a business operation by itself (that is <see cref="CurrentAuthorization"/>).
/// Inactivity is measured with an injected monotonic clock. Locking never discards anything: drafts stay in memory and the same user resumes where they were.
/// All members are meant to be called on the UI thread.
/// </summary>
public sealed class UserSession
{
    /// <summary>Longest a clean shutdown waits for its audit entry. It is best-effort and must never keep the application open.</summary>
    public static readonly TimeSpan ShutdownAuditTimeout = TimeSpan.FromSeconds(3);

    private readonly SessionService _sessions;
    private readonly AccountService _accounts;
    private readonly IMonotonicClock _clock;
    private readonly TimeSpan _timeout;
    private readonly UnsavedWorkRegistry _unsaved;
    private readonly IUnsavedWorkPrompt _prompt;
    private readonly IDiscardConfirmation _confirmation;
    private readonly int _minimumPasswordLength;

    private Principal _principal;
    private Principal? _pendingPasswordChange;
    private TimeSpan _lastActivity;

    public UserSession(
        Principal principal,
        SessionService sessions,
        AccountService accounts,
        IMonotonicClock clock,
        AuthenticationPolicy policy,
        UnsavedWorkRegistry unsaved,
        IUnsavedWorkPrompt prompt,
        IDiscardConfirmation confirmation)
    {
        _principal = principal ?? throw new ArgumentNullException(nameof(principal));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(policy);
        _unsaved = unsaved ?? throw new ArgumentNullException(nameof(unsaved));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));

        _timeout = policy.InactivityLockTimeout;
        _minimumPasswordLength = policy.MinimumPasswordLength;
        _lastActivity = _clock.Elapsed;
    }

    /// <summary>Raised when <see cref="State"/>, <see cref="Message"/>, <see cref="PasswordChangeRequired"/> or the principal changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised once, when the session has ended.</summary>
    public event EventHandler<SessionEndReason>? Ended;

    /// <summary>The UI snapshot of the signed-in user. Not an authorization.</summary>
    public Principal Principal => _principal;

    public SessionState State { get; private set; } = SessionState.Active;

    public SessionLockReason? LockReason { get; private set; }

    /// <summary>True after a correct unlock password when the user must first choose a new one; normal access stays closed until they have.</summary>
    public bool PasswordChangeRequired => _pendingPasswordChange is not null;

    /// <summary>A short, generic message for the lock screen. Never reveals more than the sign-in window would.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>How many session audit entries could not be written (the database was unreachable). The session event still took effect.</summary>
    public int AuditFailures { get; private set; }

    /// <summary>Input happened (keyboard, mouse, touch). Ignored while locked: input on the lock screen never resumes anything by itself.</summary>
    public void RecordActivity()
    {
        if (State == SessionState.Active)
        {
            _lastActivity = _clock.Elapsed;
        }
    }

    /// <summary>Called regularly by a timer. Locks the session when the user has been idle for at least the configured time. Returns true if it locked.</summary>
    public async Task<bool> TickAsync()
    {
        if (State != SessionState.Active || _clock.Elapsed - _lastActivity < _timeout)
        {
            return false;
        }

        await LockAsync(SessionLockReason.Inactivity);
        return true;
    }

    /// <summary>Locks an active session: content is hidden at once (before anything is written), then the event is audited best-effort. Does nothing when not active.</summary>
    public async Task LockAsync(SessionLockReason reason)
    {
        if (State != SessionState.Active)
        {
            return;
        }

        State = SessionState.Locked;
        LockReason = reason;
        Message = string.Empty;
        RaiseChanged();

        await AuditAsync(ct => _sessions.RecordLockedAsync(_principal, reason, ct));
    }

    /// <summary>A Windows session event (lock, switch away, ...). Resuming Windows never unlocks the session: only a password does.</summary>
    public Task OnWindowsSessionEventAsync(SessionSwitchReason reason) =>
        WindowsSessionEvents.LockReasonFor(reason) is { } lockReason ? LockAsync(lockReason) : Task.CompletedTask;

    /// <summary>Tries to resume a locked session with the user's password.</summary>
    public async Task<UnlockStatus?> UnlockAsync(string? password, CancellationToken cancellationToken = default)
    {
        if (State != SessionState.Locked || PasswordChangeRequired)
        {
            return null;
        }

        var result = await _sessions.UnlockAsync(_principal, password, cancellationToken);
        switch (result.Status)
        {
            case UnlockStatus.Succeeded:
                // Department, role and name as they are in the database now.
                _principal = result.Principal!;
                Resume();
                break;
            case UnlockStatus.PasswordChangeRequired:
                _principal = result.Principal!;
                _pendingPasswordChange = result.Principal;
                Message = "این گذرواژه یک‌بار مصرف است. پیش از ادامه، گذرواژه دلخواه خود را انتخاب کنید.";
                RaiseChanged();
                break;
            case UnlockStatus.SessionEnded:
                End(SessionEndReason.AccountUnavailable);
                break;
            default:
                Message = "گذرواژه نادرست است.";
                RaiseChanged();
                break;
        }

        return result.Status;
    }

    /// <summary>The mandatory password change after an unlock. Success restores normal access; the account being deactivated meanwhile ends the session.</summary>
    public async Task ChangePasswordAsync(string? currentPassword, string? newPassword, string? repeatedPassword, CancellationToken cancellationToken = default)
    {
        if (State != SessionState.Locked || _pendingPasswordChange is null)
        {
            return;
        }

        if (newPassword != repeatedPassword)
        {
            Message = "تکرار گذرواژه با گذرواژه یکسان نیست.";
            RaiseChanged();
            return;
        }

        var result = await _accounts.ChangeOwnPasswordAsync(_pendingPasswordChange, currentPassword, newPassword, cancellationToken);
        switch (result.Status)
        {
            case AccountStatus.Succeeded:
                _principal = _pendingPasswordChange with { MustChangePassword = false };
                _pendingPasswordChange = null;
                Resume();
                break;
            case AccountStatus.WrongCurrentPassword:
                Message = "گذرواژه فعلی نادرست است.";
                RaiseChanged();
                break;
            case AccountStatus.PasswordUnchanged:
                Message = "گذرواژه جدید باید با گذرواژه فعلی متفاوت باشد.";
                RaiseChanged();
                break;
            case AccountStatus.PasswordRejected:
                Message = result.PasswordError switch
                {
                    PasswordPolicyError.TooShort => $"گذرواژه باید دست‌کم {_minimumPasswordLength} نویسه باشد.",
                    PasswordPolicyError.ContainsWhitespace => "گذرواژه نباید شامل فاصله، تب یا خط جدید (هیچ نویسهٔ خالی) باشد.",
                    _ => "گذرواژه را وارد کنید.",
                };
                RaiseChanged();
                break;
            default:
                End(SessionEndReason.AccountUnavailable);
                break;
        }
    }

    /// <summary>
    /// Explicit sign-out. Unsaved changes are settled first (Save only where supported, Discard or Cancel); Cancel leaves the user in the session. Returns false
    /// when the user cancelled. Only an active session signs out; a locked one is left through <see cref="SwitchUserAsync"/> or by unlocking.
    /// </summary>
    public async Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (State != SessionState.Active)
        {
            return false;
        }

        if (!await _unsaved.ResolveAsync(_prompt, cancellationToken))
        {
            return false;
        }

        await AuditAsync(ct => _sessions.RecordSignedOutAsync(_principal, ct));
        End(SessionEndReason.SignedOut);
        return true;
    }

    /// <summary>
    /// "Sign in as another user" from the lock screen. When there are unsaved drafts the explicit confirmation is required first; declining leaves the session
    /// locked and everything in place. Returns false when the user declined.
    /// </summary>
    public async Task<bool> SwitchUserAsync(CancellationToken cancellationToken = default)
    {
        if (State != SessionState.Locked)
        {
            return false;
        }

        if (!await ConfirmDiscardIfNeededAsync())
        {
            return false;
        }

        _unsaved.DiscardAll();
        await AuditAsync(ct => _sessions.RecordUserSwitchedAsync(_principal, ct));
        End(SessionEndReason.SwitchedUser);
        return true;
    }

    /// <summary>
    /// The user closes the application. An active session settles unsaved changes like a sign-out; a locked one needs the discard confirmation when there are
    /// drafts. Then the in-memory session ends and the clean shutdown is audited best-effort. Returns false when the user cancelled.
    /// </summary>
    public async Task<bool> CloseApplicationAsync(CancellationToken cancellationToken = default)
    {
        switch (State)
        {
            case SessionState.Ended:
                return true;
            case SessionState.Active:
                if (!await _unsaved.ResolveAsync(_prompt, cancellationToken))
                {
                    return false;
                }

                break;
            default:
                if (!await ConfirmDiscardIfNeededAsync())
                {
                    return false;
                }

                _unsaved.DiscardAll();
                break;
        }

        using var timeout = new CancellationTokenSource(ShutdownAuditTimeout);
        await AuditAsync(ct => _sessions.RecordApplicationSessionEndedAsync(_principal, ct), timeout.Token);
        End(SessionEndReason.ApplicationClosed);
        return true;
    }

    /// <summary>
    /// The application is exiting without a normal close (for example Windows is shutting down). Ends the session without asking anything and audits the shutdown
    /// best-effort, waiting at most <see cref="ShutdownAuditTimeout"/>. Does nothing when the session has already ended. A crash never reaches this method.
    /// </summary>
    public void EndForShutdown()
    {
        if (State == SessionState.Ended)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(ShutdownAuditTimeout);
        try
        {
            Task.Run(() => _sessions.RecordApplicationSessionEndedAsync(_principal, timeout.Token)).Wait(ShutdownAuditTimeout);
        }
        catch (Exception)
        {
            AuditFailures++;
        }

        End(SessionEndReason.ApplicationClosed);
    }

    private async Task<bool> ConfirmDiscardIfNeededAsync() =>
        !_unsaved.HasUnsavedChanges || await _confirmation.ConfirmDiscardAsync();

    private void Resume()
    {
        State = SessionState.Active;
        LockReason = null;
        Message = string.Empty;
        _lastActivity = _clock.Elapsed;
        RaiseChanged();
    }

    private void End(SessionEndReason reason)
    {
        if (State == SessionState.Ended)
        {
            return;
        }

        State = SessionState.Ended;
        _pendingPasswordChange = null;
        Message = string.Empty;
        RaiseChanged();
        Ended?.Invoke(this, reason);
    }

    // A lost audit entry must not stop a lock, a sign-out or a shutdown from happening; it is counted so it can be noticed.
    private async Task AuditAsync(Func<CancellationToken, Task> record, CancellationToken cancellationToken = default)
    {
        try
        {
            await record(cancellationToken);
        }
        catch (Exception)
        {
            AuditFailures++;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Which Windows session events lock the application (Decision Log #74).</summary>
public static class WindowsSessionEvents
{
    /// <summary>
    /// Lock: Windows is locked. Session switch: the user session is disconnected (fast user switching to another account, or a remote disconnect). Everything that
    /// brings the user back (unlock, connect, logon) returns null: it never unlocks the application.
    /// </summary>
    public static SessionLockReason? LockReasonFor(SessionSwitchReason reason) => reason switch
    {
        SessionSwitchReason.SessionLock => SessionLockReason.WindowsLock,
        SessionSwitchReason.ConsoleDisconnect => SessionLockReason.UserSessionSwitch,
        SessionSwitchReason.RemoteDisconnect => SessionLockReason.UserSessionSwitch,
        _ => null,
    };
}
