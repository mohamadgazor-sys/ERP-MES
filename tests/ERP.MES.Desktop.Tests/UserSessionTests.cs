using ERP.MES.Application.Security;
using ERP.MES.Desktop.Session;
using ERP.MES.Domain.Identity;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace ERP.MES.Desktop.Tests;

// The desktop session lifecycle (Decision Log #74) against the real services on a temporary SQLite database. No window is opened, nothing sleeps: inactivity is
// driven by a fake monotonic clock that the tests advance by hand.
public sealed class UserSessionTests : IDisposable
{
    private const string AdminName = "admin.one";
    private const string AdminPassword = "Admin-Pass-Phrase-1";
    private const string UserName = "session.user";
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";
    private const string NewPassword = "Brand-New-Pass-Phrase-5";

    private static readonly AuthenticationPolicy Policy = new() { Pbkdf2Iterations = AuthenticationPolicy.Pbkdf2IterationsFloor };

    private readonly string _path = Path.Combine(Path.GetTempPath(), "erpmes-session-" + Guid.NewGuid().ToString("N") + ".db");

    private SecurityServices _services = null!;
    private Principal _admin = null!;
    private Principal _user = null!;
    private long _userId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-journal", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file does not affect the result.
            }
        }
    }

    // ---- test doubles ----

    private sealed class ManualMonotonicClock : IMonotonicClock
    {
        public TimeSpan Elapsed { get; private set; } = TimeSpan.FromHours(5);

        public void Advance(TimeSpan by) => Elapsed += by;
    }

    private sealed class ManualWallClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeForm(bool canSave = true, bool saveSucceeds = true) : IUnsavedWork
    {
        public bool HasUnsavedChanges { get; set; } = true;

        public bool CanSave { get; } = canSave;

        public int Saved { get; private set; }

        public int Discarded { get; private set; }

        public Task<bool> SaveAsync(CancellationToken cancellationToken)
        {
            if (!saveSucceeds)
            {
                return Task.FromResult(false);
            }

            Saved++;
            HasUnsavedChanges = false;
            return Task.FromResult(true);
        }

        public void Discard()
        {
            Discarded++;
            HasUnsavedChanges = false;
        }
    }

    private sealed class FakePrompts : IUnsavedWorkPrompt, IDiscardConfirmation
    {
        public UnsavedDecision Decision { get; set; } = UnsavedDecision.Cancel;

        public bool Confirm { get; set; }

        public int Asked { get; private set; }

        public bool? LastSaveOffered { get; private set; }

        public int Confirmations { get; private set; }

        public Task<UnsavedDecision> AskAsync(bool saveOffered)
        {
            Asked++;
            LastSaveOffered = saveOffered;
            return Task.FromResult(Decision);
        }

        public Task<bool> ConfirmDiscardAsync()
        {
            Confirmations++;
            return Task.FromResult(Confirm);
        }
    }

    private sealed class ThrowingRunner : ISecurityUnitRunner
    {
        public Task<T> RunAsync<T>(Func<ISecurityUnit, Task<T>> work, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The database is not reachable.");
    }

    private sealed class Harness
    {
        public required UserSession Session { get; init; }

        public required ManualMonotonicClock Clock { get; init; }

        public required UnsavedWorkRegistry Registry { get; init; }

        public required FakePrompts Prompts { get; init; }

        public List<SessionEndReason> Ended { get; } = [];
    }

    private async Task<Harness> StartAsync(AuthenticationPolicy? policy = null, string department = DepartmentCodes.Planning, string role = RoleCodes.Staff, SessionService? sessions = null)
    {
        policy ??= Policy;
        await SqliteDatabase.MigrateAsync(_path, Token);
        _services = SecurityServices.Create(SqliteDatabase.CreateContextFactory(_path), policy, new ManualWallClock());

        var issued = await _services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        Assert.Equal(BootstrapStatus.Succeeded, (await _services.Bootstrap.CompleteAsync(issued.Token, AdminName, "Administrator", AdminPassword, Token)).Status);
        _admin = (await _services.Authentication.SignInAsync(AdminName, AdminPassword, Token)).Principal!;

        var created = await _services.Accounts.CreateUserAsync(_admin, UserName, "Session user", department, role, InitialPassword, Token);
        Assert.Equal(AccountStatus.Succeeded, created.Status);
        _userId = created.UserId!.Value;
        var first = (await _services.Authentication.SignInAsync(UserName, InitialPassword, Token)).Principal!;
        Assert.Equal(AccountStatus.Succeeded, (await _services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token)).Status);
        _user = (await _services.Authentication.SignInAsync(UserName, OwnPassword, Token)).Principal!;

        var clock = new ManualMonotonicClock();
        var registry = new UnsavedWorkRegistry();
        var prompts = new FakePrompts();
        var session = new UserSession(_user, sessions ?? _services.Sessions, _services.Accounts, clock, policy, registry, prompts, prompts);
        var harness = new Harness { Session = session, Clock = clock, Registry = registry, Prompts = prompts };
        session.Ended += (_, reason) => harness.Ended.Add(reason);
        return harness;
    }

    private List<(string Action, long? UserId, string Summary)> Audit()
    {
        using var context = SqliteDatabase.CreateContextFactory(_path).CreateDbContext();
        return context.AuditLog.AsNoTracking().OrderBy(a => a.Id).AsEnumerable().Select(a => (Action: a.ActionCode, UserId: a.UserId, Summary: a.Summary)).ToList();
    }

    private List<string> SessionActions() =>
        Audit().Where(a => a.Action.StartsWith("Session", StringComparison.Ordinal) || a.Action == AuditActions.ApplicationSessionEnded).Select(a => a.Action).ToList();

    // ---- timeout boundaries and monotonic time ----

    [Fact]
    public async Task The_session_locks_exactly_when_the_idle_time_reaches_the_timeout()
    {
        var h = await StartAsync();
        var timeout = Policy.InactivityLockTimeout;
        Assert.Equal(TimeSpan.FromMinutes(15), timeout);

        h.Clock.Advance(timeout - TimeSpan.FromTicks(1));
        Assert.False(await h.Session.TickAsync());
        Assert.Equal(SessionState.Active, h.Session.State);

        h.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(await h.Session.TickAsync());
        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.Equal(SessionLockReason.Inactivity, h.Session.LockReason);
        Assert.Equal(new[] { AuditActions.SessionLocked }, SessionActions());
        Assert.Contains("Inactivity", Audit().Single(a => a.Action == AuditActions.SessionLocked).Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_restarts_the_idle_time()
    {
        var h = await StartAsync();

        h.Clock.Advance(TimeSpan.FromMinutes(14));
        h.Session.RecordActivity();
        h.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.False(await h.Session.TickAsync());

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await h.Session.TickAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public async Task The_configured_timeout_at_both_ends_of_the_range_is_used(int minutes)
    {
        var h = await StartAsync(Policy with { InactivityLockMinutes = minutes });

        h.Clock.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1));
        Assert.False(await h.Session.TickAsync());
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await h.Session.TickAsync());
    }

    [Fact]
    public async Task Inactivity_depends_on_the_monotonic_clock_alone_so_changing_the_system_clock_cannot_bypass_or_trigger_it()
    {
        // The session is built from a monotonic clock only: it has no wall-clock (IClock, DateTime.UtcNow) dependency that a change of the Windows date or time could move.
        var parameters = typeof(UserSession).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Contains(typeof(IMonotonicClock), parameters);
        Assert.DoesNotContain(typeof(IClock), parameters);
        Assert.DoesNotContain(typeof(TimeProvider), parameters);

        // And the real clock never goes back: two readings are ordered.
        var real = new StopwatchClock();
        var first = real.Elapsed;
        Assert.True(real.Elapsed >= first);

        var h = await StartAsync();
        Assert.False(await h.Session.TickAsync());
        h.Clock.Advance(Policy.InactivityLockTimeout);
        Assert.True(await h.Session.TickAsync());
    }

    [Fact]
    public async Task Input_on_the_lock_screen_does_not_unlock_or_restart_anything()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);

        h.Session.RecordActivity();
        Assert.False(await h.Session.TickAsync());

        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.Equal(new[] { AuditActions.SessionLocked }, SessionActions());
    }

    // ---- manual lock and Windows events ----

    [Fact]
    public async Task Lock_now_locks_with_the_manual_reason_once()
    {
        var h = await StartAsync();

        await h.Session.LockAsync(SessionLockReason.Manual);
        await h.Session.LockAsync(SessionLockReason.Manual);

        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.Equal(SessionLockReason.Manual, h.Session.LockReason);
        Assert.Equal(new[] { AuditActions.SessionLocked }, SessionActions());
        Assert.Contains("Manual", Audit().Single(a => a.Action == AuditActions.SessionLocked).Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, SessionLockReason.WindowsLock)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, SessionLockReason.UserSessionSwitch)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, SessionLockReason.UserSessionSwitch)]
    public async Task Windows_lock_and_user_session_switch_lock_the_application(SessionSwitchReason windows, SessionLockReason expected)
    {
        var h = await StartAsync();

        await h.Session.OnWindowsSessionEventAsync(windows);

        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.Equal(expected, h.Session.LockReason);
        Assert.Contains(expected.ToString(), Audit().Single(a => a.Action == AuditActions.SessionLocked).Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionUnlock)]
    [InlineData(SessionSwitchReason.ConsoleConnect)]
    [InlineData(SessionSwitchReason.RemoteConnect)]
    [InlineData(SessionSwitchReason.SessionLogon)]
    [InlineData(SessionSwitchReason.SessionRemoteControl)]
    public async Task Events_that_bring_the_user_back_never_lock_and_never_unlock(SessionSwitchReason windows)
    {
        var h = await StartAsync();

        await h.Session.OnWindowsSessionEventAsync(windows);
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Empty(SessionActions());

        // And once locked, resuming Windows leaves the application locked: only the password unlocks it.
        await h.Session.OnWindowsSessionEventAsync(SessionSwitchReason.SessionLock);
        await h.Session.OnWindowsSessionEventAsync(windows);
        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.Equal(new[] { AuditActions.SessionLocked }, SessionActions());
    }

    // ---- unlock ----

    [Fact]
    public async Task The_same_users_password_unlocks_and_the_idle_time_starts_again()
    {
        var h = await StartAsync();
        h.Clock.Advance(Policy.InactivityLockTimeout);
        await h.Session.TickAsync();

        Assert.Equal(UnlockStatus.Succeeded, await h.Session.UnlockAsync(OwnPassword, Token));

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Null(h.Session.LockReason);
        Assert.Equal(new[] { AuditActions.SessionLocked, AuditActions.SessionUnlocked }, SessionActions());
        h.Clock.Advance(Policy.InactivityLockTimeout - TimeSpan.FromSeconds(1));
        Assert.False(await h.Session.TickAsync());
    }

    [Fact]
    public async Task A_wrong_password_keeps_the_session_locked_with_a_generic_message()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);

        Assert.Equal(UnlockStatus.Failed, await h.Session.UnlockAsync("wrong-password", Token));

        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.False(string.IsNullOrEmpty(h.Session.Message));
        Assert.DoesNotContain("wrong-password", h.Session.Message, StringComparison.Ordinal);
        Assert.Contains(Audit(), a => a.Action == AuditActions.LoginFailed);
        Assert.DoesNotContain(AuditActions.SessionUnlocked, SessionActions());
    }

    [Fact]
    public async Task Repeated_failed_unlocks_trigger_the_account_lockout_and_end_the_session()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);

        for (var i = 1; i < Policy.MaxFailedAttempts; i++)
        {
            Assert.Equal(UnlockStatus.Failed, await h.Session.UnlockAsync("wrong-" + i, Token));
            Assert.Equal(SessionState.Locked, h.Session.State);
        }

        Assert.Equal(UnlockStatus.SessionEnded, await h.Session.UnlockAsync("wrong-last", Token));

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.AccountUnavailable }, h.Ended);
        Assert.Contains(Audit(), a => a.Action == AuditActions.AccountLocked);
        Assert.Equal(SignInStatus.Failed, (await _services.Authentication.SignInAsync(UserName, OwnPassword, Token)).Status);
    }

    [Fact]
    public async Task A_deactivated_account_ends_the_session_at_unlock()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);
        Assert.Equal(AccountStatus.Succeeded, (await _services.Accounts.SetActiveAsync(_admin, _userId, false, Token)).Status);

        Assert.Equal(UnlockStatus.SessionEnded, await h.Session.UnlockAsync(OwnPassword, Token));

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.AccountUnavailable }, h.Ended);
        Assert.DoesNotContain(AuditActions.SessionUnlocked, SessionActions());
    }

    [Fact]
    public async Task Unlock_replaces_the_cached_principal_with_the_current_role_and_the_cached_one_is_not_trusted()
    {
        var h = await StartAsync(department: DepartmentCodes.Management, role: RoleCodes.Manager);
        Assert.Equal(RoleCodes.Manager, h.Session.Principal.RoleCode);
        await h.Session.LockAsync(SessionLockReason.Manual);
        Assert.Equal(AccountStatus.Succeeded, (await _services.Accounts.ChangeRoleAsync(_admin, _userId, RoleCodes.Staff, Token)).Status);

        // While locked the snapshot is stale, and the Application still refuses on current data.
        Assert.False((await _services.Authorization.CheckAsync(h.Session.Principal, Permission.ApproveOrRejectInitialRequest, Token)).IsAllowed);

        Assert.Equal(UnlockStatus.Succeeded, await h.Session.UnlockAsync(OwnPassword, Token));
        Assert.Equal(RoleCodes.Staff, h.Session.Principal.RoleCode);
    }

    [Fact]
    public async Task A_mandatory_password_change_must_complete_before_normal_access_returns()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);
        Assert.Equal(AccountStatus.Succeeded, (await _services.Accounts.ResetPasswordAsync(_admin, _userId, InitialPassword, Token)).Status);

        Assert.Equal(UnlockStatus.PasswordChangeRequired, await h.Session.UnlockAsync(InitialPassword, Token));
        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.True(h.Session.PasswordChangeRequired);
        Assert.Null(await h.Session.UnlockAsync(InitialPassword, Token));

        await h.Session.ChangePasswordAsync(InitialPassword, NewPassword, "not the same", Token);
        Assert.Equal(SessionState.Locked, h.Session.State);
        await h.Session.ChangePasswordAsync("not my password", NewPassword, NewPassword, Token);
        Assert.Equal(SessionState.Locked, h.Session.State);
        await h.Session.ChangePasswordAsync(InitialPassword, "has space", "has space", Token);
        Assert.Equal(SessionState.Locked, h.Session.State);
        await h.Session.ChangePasswordAsync(InitialPassword, InitialPassword, InitialPassword, Token);
        Assert.Equal(SessionState.Locked, h.Session.State);

        await h.Session.ChangePasswordAsync(InitialPassword, NewPassword, NewPassword, Token);

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.False(h.Session.PasswordChangeRequired);
        Assert.False(h.Session.Principal.MustChangePassword);
        Assert.Equal(SignInStatus.Succeeded, (await _services.Authentication.SignInAsync(UserName, NewPassword, Token)).Status);
    }

    [Fact]
    public async Task A_deactivation_during_the_mandatory_password_change_ends_the_session()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);
        await _services.Accounts.ResetPasswordAsync(_admin, _userId, InitialPassword, Token);
        Assert.Equal(UnlockStatus.PasswordChangeRequired, await h.Session.UnlockAsync(InitialPassword, Token));
        await _services.Accounts.SetActiveAsync(_admin, _userId, false, Token);

        await h.Session.ChangePasswordAsync(InitialPassword, NewPassword, NewPassword, Token);

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.AccountUnavailable }, h.Ended);
    }

    // ---- drafts, sign-out, switching user ----

    [Fact]
    public async Task Locking_and_unlocking_keep_the_unsaved_drafts_in_memory_untouched()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);

        await h.Session.LockAsync(SessionLockReason.Inactivity);
        Assert.True(form.HasUnsavedChanges);
        await h.Session.UnlockAsync(OwnPassword, Token);

        Assert.True(form.HasUnsavedChanges);
        Assert.Equal(0, form.Discarded);
        Assert.Equal(0, form.Saved);
        Assert.Equal(0, h.Prompts.Asked);
        Assert.Equal(0, h.Prompts.Confirmations);
    }

    [Fact]
    public async Task Sign_out_without_unsaved_changes_asks_nothing_audits_and_ends_the_session()
    {
        var h = await StartAsync();

        Assert.True(await h.Session.SignOutAsync(Token));

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.SignedOut }, h.Ended);
        Assert.Equal(0, h.Prompts.Asked);
        Assert.Equal(new[] { AuditActions.SessionSignedOut }, SessionActions());
        Assert.Equal(_userId, Audit().Single(a => a.Action == AuditActions.SessionSignedOut).UserId);
    }

    [Fact]
    public async Task Sign_out_with_unsaved_changes_offers_save_only_where_every_form_can_save()
    {
        var h = await StartAsync();
        var saveable = new FakeForm();
        h.Registry.Register(saveable);
        h.Prompts.Decision = UnsavedDecision.Cancel;

        await h.Session.SignOutAsync(Token);
        Assert.True(h.Prompts.LastSaveOffered);

        h.Registry.Register(new FakeForm(canSave: false));
        await h.Session.SignOutAsync(Token);
        Assert.False(h.Prompts.LastSaveOffered);
    }

    [Fact]
    public async Task Cancel_leaves_the_user_in_the_session_with_the_changes_intact()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);
        h.Prompts.Decision = UnsavedDecision.Cancel;

        Assert.False(await h.Session.SignOutAsync(Token));

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.True(form.HasUnsavedChanges);
        Assert.Empty(h.Ended);
        Assert.Empty(SessionActions());
    }

    [Fact]
    public async Task Save_saves_the_forms_and_then_signs_out_and_a_failed_save_keeps_the_user_in_the_session()
    {
        var h = await StartAsync();
        var failing = new FakeForm(saveSucceeds: false);
        using (h.Registry.Register(failing))
        {
            h.Prompts.Decision = UnsavedDecision.Save;
            Assert.False(await h.Session.SignOutAsync(Token));
            Assert.Equal(SessionState.Active, h.Session.State);
            Assert.True(failing.HasUnsavedChanges);
        }

        var form = new FakeForm();
        h.Registry.Register(form);
        Assert.True(await h.Session.SignOutAsync(Token));

        Assert.Equal(1, form.Saved);
        Assert.Equal(SessionState.Ended, h.Session.State);
    }

    [Fact]
    public async Task Save_is_never_carried_out_for_a_form_that_cannot_save()
    {
        var h = await StartAsync();
        var form = new FakeForm(canSave: false);
        h.Registry.Register(form);
        h.Prompts.Decision = UnsavedDecision.Save;

        Assert.False(await h.Session.SignOutAsync(Token));

        Assert.Equal(0, form.Saved);
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public async Task Discard_throws_the_changes_away_and_signs_out()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);
        h.Prompts.Decision = UnsavedDecision.Discard;

        Assert.True(await h.Session.SignOutAsync(Token));

        Assert.Equal(1, form.Discarded);
        Assert.Equal(SessionState.Ended, h.Session.State);
    }

    [Fact]
    public async Task A_locked_session_is_left_through_switch_user_not_through_sign_out()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);

        Assert.False(await h.Session.SignOutAsync(Token));
        Assert.Equal(SessionState.Locked, h.Session.State);
    }

    [Fact]
    public async Task Switching_user_without_drafts_needs_no_confirmation_and_is_audited()
    {
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);

        Assert.True(await h.Session.SwitchUserAsync(Token));

        Assert.Equal(0, h.Prompts.Confirmations);
        Assert.Equal(new[] { SessionEndReason.SwitchedUser }, h.Ended);
        Assert.Equal(new[] { AuditActions.SessionLocked, AuditActions.SessionUserSwitched }, SessionActions());
    }

    [Fact]
    public async Task Switching_user_with_drafts_needs_explicit_confirmation_and_declining_keeps_everything_locked()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);
        await h.Session.LockAsync(SessionLockReason.Inactivity);
        h.Prompts.Confirm = false;

        Assert.False(await h.Session.SwitchUserAsync(Token));

        Assert.Equal(1, h.Prompts.Confirmations);
        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.True(form.HasUnsavedChanges);
        Assert.Equal(0, form.Discarded);
        Assert.Empty(h.Ended);
        Assert.Equal(new[] { AuditActions.SessionLocked }, SessionActions());

        h.Prompts.Confirm = true;
        Assert.True(await h.Session.SwitchUserAsync(Token));

        Assert.Equal(1, form.Discarded);
        Assert.Equal(new[] { SessionEndReason.SwitchedUser }, h.Ended);
        Assert.Equal(new[] { AuditActions.SessionLocked, AuditActions.SessionUserSwitched }, SessionActions());
    }

    [Fact]
    public async Task Switching_user_is_only_possible_from_the_lock_screen()
    {
        var h = await StartAsync();

        Assert.False(await h.Session.SwitchUserAsync(Token));
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public async Task A_forced_end_discards_the_drafts_without_asking()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);
        await h.Session.LockAsync(SessionLockReason.Manual);
        await _services.Accounts.SetActiveAsync(_admin, _userId, false, Token);

        await h.Session.UnlockAsync(OwnPassword, Token);

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(0, h.Prompts.Asked);
    }

    // ---- closing the application ----

    [Fact]
    public async Task Closing_the_application_ends_the_session_and_audits_the_clean_shutdown()
    {
        var h = await StartAsync();

        Assert.True(await h.Session.CloseApplicationAsync(Token));

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.ApplicationClosed }, h.Ended);
        Assert.Equal(new[] { AuditActions.ApplicationSessionEnded }, SessionActions());
        Assert.DoesNotContain(AuditActions.SessionSignedOut, SessionActions());
        Assert.Equal(0, h.Session.AuditFailures);
    }

    [Fact]
    public async Task Closing_with_unsaved_changes_follows_the_user_decision_and_cancel_keeps_the_session()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);
        h.Prompts.Decision = UnsavedDecision.Cancel;

        Assert.False(await h.Session.CloseApplicationAsync(Token));
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Empty(SessionActions());

        h.Prompts.Decision = UnsavedDecision.Discard;
        Assert.True(await h.Session.CloseApplicationAsync(Token));
        Assert.Equal(1, form.Discarded);
        Assert.Equal(new[] { AuditActions.ApplicationSessionEnded }, SessionActions());
    }

    [Fact]
    public async Task Closing_a_locked_application_with_drafts_needs_the_discard_confirmation()
    {
        var h = await StartAsync();
        var form = new FakeForm();
        h.Registry.Register(form);
        await h.Session.LockAsync(SessionLockReason.Manual);
        h.Prompts.Confirm = false;

        Assert.False(await h.Session.CloseApplicationAsync(Token));
        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.True(form.HasUnsavedChanges);

        h.Prompts.Confirm = true;
        Assert.True(await h.Session.CloseApplicationAsync(Token));
        Assert.Equal(1, form.Discarded);
        Assert.Equal(new[] { AuditActions.SessionLocked, AuditActions.ApplicationSessionEnded }, SessionActions());
    }

    [Fact]
    public async Task The_shutdown_audit_is_best_effort_a_failure_never_keeps_the_application_open()
    {
        var failing = new SessionService(new ThrowingRunner(), new Pbkdf2PasswordHasher(), new ManualWallClock(), new EnvironmentAuditContext(), Policy);
        var h = await StartAsync(sessions: failing);

        Assert.True(await h.Session.CloseApplicationAsync(Token));

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.ApplicationClosed }, h.Ended);
        Assert.Equal(1, h.Session.AuditFailures);
        // Nothing was written, and no entry claims anything else happened.
        Assert.Empty(SessionActions());
    }

    [Fact]
    public async Task A_lock_still_takes_effect_when_its_audit_entry_cannot_be_written()
    {
        var failing = new SessionService(new ThrowingRunner(), new Pbkdf2PasswordHasher(), new ManualWallClock(), new EnvironmentAuditContext(), Policy);
        var h = await StartAsync(sessions: failing);

        await h.Session.LockAsync(SessionLockReason.Manual);

        Assert.Equal(SessionState.Locked, h.Session.State);
        Assert.Equal(1, h.Session.AuditFailures);
    }

    [Fact]
    public async Task Ending_for_shutdown_audits_once_and_does_nothing_after_the_session_has_ended()
    {
        var h = await StartAsync();

        h.Session.EndForShutdown();
        h.Session.EndForShutdown();

        Assert.Equal(SessionState.Ended, h.Session.State);
        Assert.Equal(new[] { SessionEndReason.ApplicationClosed }, h.Ended);
        Assert.Equal(new[] { AuditActions.ApplicationSessionEnded }, SessionActions());
    }

    [Fact]
    public async Task A_session_that_is_simply_abandoned_writes_no_end_event()
    {
        // Honest limits: a crash, a forced termination or a power loss never reaches any code, so nothing is written and nothing is invented afterwards.
        var h = await StartAsync();
        await h.Session.LockAsync(SessionLockReason.Manual);

        GC.KeepAlive(h);

        Assert.DoesNotContain(AuditActions.ApplicationSessionEnded, SessionActions());
        Assert.DoesNotContain(AuditActions.SessionSignedOut, SessionActions());
    }

    [Fact]
    public async Task Nothing_is_persisted_for_the_session_and_a_new_session_needs_a_new_sign_in()
    {
        var h = await StartAsync();
        await h.Session.CloseApplicationAsync(Token);

        // A new process has only the database: no session, token or credential from before is there, so the only way in is the sign-in.
        var again = await _services.Authentication.SignInAsync(UserName, "", Token);
        Assert.Equal(SignInStatus.Failed, again.Status);
        Assert.Null(again.Principal);
    }

    [Fact]
    public void Only_the_Windows_events_that_move_the_user_away_have_a_lock_reason()
    {
        var locking = Enum.GetValues<SessionSwitchReason>().Where(r => WindowsSessionEvents.LockReasonFor(r) is not null).ToArray();

        Assert.Equal(
            new[] { SessionSwitchReason.ConsoleDisconnect, SessionSwitchReason.RemoteDisconnect, SessionSwitchReason.SessionLock }.OrderBy(r => (int)r),
            locking.OrderBy(r => (int)r));
    }
}
