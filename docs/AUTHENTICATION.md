# Sign-in and Authentication (implementation notes)

| | |
|---|---|
| **Status** | Sign-in (#19) corrected after the owner's review (Decision Log #61-#71); password policy #72; desktop session lifecycle #74 (this document, section 8a). |
| **Scope** | Password hashing, sign-in with lockout, the one-time administrator bootstrap, administrator recovery, account administration with the last-administrator rule, the default-deny authorization rule set, the audit log, the sign-in window, the restricted Provisioning commands, real fictional demonstration accounts, and real foreign keys for creators and approvers. |
| **Basis** | Decision Log #26, #27, #30, #34, #39-#42, #47, #48 and the owner decisions #61-#71; `docs/MILESTONE_1_PLAN.md` sections 5-7; Domain and Database Specification 15; Architecture Specification 3.3 and 17; `docs/FOUR_DAY_DEMO_PLAN.md` T5 and T6. |
| **Not decided here** | The production database provider/version, the full permission matrix beyond the approved rows, the production-only parts of the session policy (section 11), the customer/request/queue/QC screens. |

The approved design is implemented as written. Nothing was weakened because the demonstration database is resettable; the demonstration-specific parts (`Infrastructure/Demo`, `DemoDatabase`) use the production use cases.

## 1. Where things live (Decision Log #61)

| Layer | Holds | Does not hold |
|---|---|---|
| **Domain** (`ERP.MES.Domain/Identity`) | `User`, `Role`, `Department`, the stable `DepartmentCodes` / `RoleCodes`, and the `Username` rule with its canonical key; their invariants (exactly one department and one role per user, non-empty names, UTC times, deactivate - never delete). | Any password, hash, salt, token, lockout counter, cryptographic code, EF Core, database or UI type. Tests enforce this boundary (reflection on the Domain assembly and a scan of its code). |
| **Application** (`ERP.MES.Application/Security`) | The use cases: `AuthenticationService`, `BootstrapService`, `RecoveryService`, `AccountService`; `AuthorizationRules`; the `AuthenticationPolicy`; password rules; the credential record `UserCredential`; `SystemSetup` and `AuditLogEntry`; the abstractions (`IPasswordHasher`, `IClock`, `IAuditContext`, `ISecurityUnit`). Time is read only through `IClock`. | EF Core, database types, cryptography implementations. |
| **Infrastructure** (`ERP.MES.Infrastructure`) | `Pbkdf2PasswordHasher`, the security unit and its EF Core mappings (provider-neutral), `AuthenticationPolicyFile`, `CredentialFile`, token handling by the Application services with SHA-256 from the base library. | Business rules. |
| **Infrastructure.Sqlite** | The migrations, `SqliteDatabase` (including the actor-reference guard) and `DemoDatabase`. | Business rules. |
| **Provisioning** | `migrate`, `issue-admin-token`, `issue-recovery-token`, `benchmark-hash`, `demo-*` commands. Restricted tool, not part of the factory-PC distribution. | |
| **Desktop** | The sign-in window and its view model, which talk only to the Application use cases and never store a password or token. | Authorization decisions. |

A user is therefore two records: the Domain `User` (who a person is in the organisation: `Users` table) and the `UserCredential` (how they sign in: `UserCredentials` table, one row per user, key = the user). A rule that spans several users - the last active Administrator cannot be removed - stays in the account-administration use case.

**No new package.** PBKDF2 (`Rfc2898DeriveBytes.Pbkdf2`), SHA-256, the random generator and constant-time comparison are in the .NET base library; settings use `System.Text.Json`. The `Microsoft.Extensions.*` packages listed in the Milestone 1 plan (options, configuration, DI) were **not** added, because each package batch needs your approval (Decision Log #43, #53).

## 2. Password hashing (Decision Log #27, #41, #42)

- PBKDF2-HMAC-SHA256, initially **600,000 iterations**, a **fresh 16-byte random salt per password**, a 32-byte derived key.
- Stored per user (in `UserCredentials`): `PasswordHash`, `PasswordSalt`, `PasswordAlgorithm` (`PBKDF2-HMAC-SHA256`), `PasswordHashVersion` (1) and `PasswordWorkFactor`. Verification uses the stored values, never a global setting, and compares in constant time.
- After a successful sign-in, a hash whose algorithm, version or work factor is below the configured baseline is re-hashed and the change is audited (`PasswordHashUpgraded`). A failed attempt upgrades nothing. The work factor is never lowered automatically; values below 310,000 are refused at start-up.
- The password is used exactly as typed (UTF-8, no trimming, no normalisation); a password with white space is rejected by the policy rather than altered.
- `benchmark-hash` times one hash on the machine where it runs (target about 500 ms on the lowest-spec supported PC, Decision Log #42). **It has not been run on that PC**: the PC is not identified yet, so 600,000 is still a starting value.

## 3. Sign-in, lockout and one-time passwords (Decision Log #40, #62, #64, #65)

- **Password policy (#72).** Minimum 6 characters (counted in characters, not bytes). **No Unicode white-space character is allowed anywhere in the password**, including the start and end: space, no-break space (U+00A0), ideographic space (U+3000), tab, line breaks and every other character that .NET `char.IsWhiteSpace` accepts. Such a password is rejected - never trimmed or normalised. Every other character is allowed and no mix of character kinds is required; the Persian zero-width non-joiner (U+200C) is not white space and stays allowed. One policy (`PasswordRules`) is applied wherever a user chooses a new password: initial password of a new account, own password change, administrator reset, bootstrap and recovery. Signing in verifies the typed text exactly and does not apply the policy, so an existing password is never refused by it. Generated demonstration passwords are 20 random characters from an alphabet without white space.
- Defaults: minimum password length 6 (see above), 5 failed attempts, 5-minute lockout, inactivity lock 15 minutes (section 8a); all configurable, none outside its range (length >= 6, attempts 3-10, lockout >= 1 minute, iterations >= 310,000, inactivity lock 1-120 minutes).
- **User names (#62).** Stored as typed, matched and made unique ignoring case through one canonical key (upper case, invariant culture) computed by the Domain `Username` and stored in a unique `UsernameKey` column, so the application and the database apply the same rule (no database collation decides). Leading or trailing white space is rejected, never trimmed; control characters, an empty name and more than 64 characters are rejected. A text that is not a valid name can belong to no user, so signing in with it simply fails. No Unicode normalization is applied (for example Arabic versus Persian letter forms): not decided.
- **One generic failure (#65).** Unknown user, wrong password, inactive account and locked account give the caller exactly the same answer (`Failed`, no principal) and the window shows one message. The audit log keeps the distinctions internally (`LoginFailed` with the reason, `AccountLocked`). **Every one of those cases performs exactly one password verification** (a dummy hash for an unknown name, the stored hash otherwise), so response time does not reveal which case it was; a test counts the verifications. An attempt on a locked account never touches the credential, so **it cannot extend the lockout**.
- The failed-attempt counter and the lockout end are columns of the credential row, so they hold across every application instance sharing the database. Concurrent failures are all counted (row version; a lost race is retried).
- **One-time credentials (#64).** The initial password of a new account and a password reset by an administrator are one-time: the user must choose their own before being let in (`MustChangePassword`). A wrong current password in the change form counts as a failed sign-in and gets the same answer as a locked account.

## 4. Administrator bootstrap (Decision Log #39, #63; plan section 5)

- A freshly migrated database has one `SystemSetup` row, `NotProvisioned`, and no users (a check constraint keeps it a single row). The application never creates an administrator by itself; `NotProvisioned` shows "contact your administrator" and no input fields.
- `issue-admin-token --database <file> [--hours N]` generates 32 random bytes from the operating-system generator, stores **only the SHA-256 digest** with an expiry (default 24 hours) and `AwaitingBootstrap`, audits it, and prints the token **once** as 64 hexadecimal digits in groups of 8 (case, dashes and spaces are ignored when typing). Issuing again replaces digest, expiry and counter, so the previous token stops working. It is refused once the system is initialised.
- Redeeming (first-run screen): input that is wrong in itself (user name, display name, password policy) is refused without using an attempt. Then **the attempt is counted in its own short transaction before the token is compared** (so guesses persist and the limit of 5 is central), the digests are compared in constant time, and the claim is a single conditional update of the setup row (row version) that also inserts the administrator, who **belongs to the System department** (#66). Of two simultaneous claims exactly one wins; the loser is told the system is already initialised.
- An expired token or the fifth wrong attempt withdraws the token (`NotProvisioned`); IT must issue a new one. After success the digest is erased and the bootstrap path is closed for good.

## 5. Administrator recovery (Decision Log #47; plan 5.7)

- `issue-recovery-token --database <file> --username <administrator> --reason <text>`: the target must be an existing Administrator account; a reason is required; the token is 256-bit random in the same format, only its digest is stored (24-hour expiry, 5 attempts) and it is printed once. The reason, machine and operating-system user are audited. Issuing again invalidates the previous token. The operator supplies the database at run time; nothing privileged is stored.
- The login screen offers "use recovery token" **only while a valid recovery token is pending**.
- Redeeming: the person enters the token and a **new** password. Same order as bootstrap (policy check, central attempt counting, constant-time comparison). Only after the token is accepted is the new password compared with the old one, so a caller without the token learns nothing about the old password; an unchanged password is refused and the token stays valid. One conditional update then sets the new hash (fresh salt, current work factor), reactivates the account, sets the Administrator role, clears the counter and lockout, erases the token and advances the administrator stamp. Of two simultaneous redemptions exactly one succeeds.
- There is no universal recovery password, master key or backdoor; tests search the source for such constructs (comments excluded).

## 6. Account administration and the last administrator

- Only the Admin role administers accounts (Domain and Database Specification 15.2). Every administrative method **reloads the acting user from the database** and checks that they are an active Administrator, so a stale or forged principal grants nothing.
- Users are never physically deleted: deactivate instead. Every foreign key restricts deletion.
- The last active Administrator can be neither deactivated nor demoted. The check and the change run in one transaction that also advances `SystemSetup.AdminChangeStamp`, so of two concurrent removals one fails and is re-evaluated against the new state (`LastAdministrator`). A refusal is audited (`LastAdminProtectionRefused`).

## 7. Authorization (default deny; Decision Log #67)

`AuthorizationRules` maps each action to departments and, where the specification says so, a role, **always by the stable codes**; a display name is a label that never decides anything (a test renames the labels, even to another department's name, and nothing changes). The rows are exactly those of Domain and Database Specification 15.2 and the provisional development map 15.4 (Decision Log #34):

| Action | Department / role |
|---|---|
| Administer users | Role Admin (any department) |
| Initial request approve/reject | Management **and** Manager |
| Drawing doability decision; define MAKE items | Drawing |
| Create/submit contract; maintain calendar; receive/edit request details; define REPAIR items and edit any item | Planning |
| Approve/reject contract; override queue order | Management |
| QC inspect/correct; maintain units of measure | QC |
| Change Planning priority; change machine rates; reopen a rejected request | Planning or Management |
| Change material price per kg | Warehouse or Management |
| Final warehouse receipt | Warehouse |
| Register downtime | Maintenance/PM |
| Create/submit a request | Central |
| Import items from Excel | Planning or Drawing |
| Maintain customers (create, rename, deactivate, reactivate; added by Decision Log #75) | Central or Planning |
| View customers and requests, read-only (added by Decision Log #75) | Every active user |

Everything else is denied, including for the Admin role (it administers accounts and, with the read-only viewing open to everyone, reads - nothing else), for an unknown permission, a missing department or role, and an inactive user. The expected table is written out a second time in the tests and compared for every department x role combination. **The customer and request screens (Decision Log #75) call them through `CurrentAuthorization` in the Application commands; the screens themselves decide nothing.** "Planning reviews downtime" is deliberately **not** mapped, because the specification leaves that workflow open.

## 8. Audit log (Decision Log #26, #68)

`AuditLog` records time, user (when known), machine, operating-system user, optional database login, action code, target and a summary. Actions: login success/failure, lockout and unlock, password change/reset/upgrade, user created/activated/deactivated/role changed, last-administrator refusal, and the bootstrap and recovery events of plan 5.6. Tokens and passwords never appear in an entry (tests check this). The acting user is nullable only because an entry can describe an event with no known user (an unknown login name, an operator running Provisioning); when there is one it is a foreign key to `Users`.

**Append-only is enforced in the application implementation** (the persistence layer rejects an update or delete of an entry) - approved for the SQLite demonstration. **Database-level protection (permissions or equivalent) is an explicit production requirement**, to be designed with the production database; SQLite also reports no database login, so that column stays empty.

## 8a. Desktop session lifecycle (Decision Log #74)

The session lives **in memory in the desktop application**. There is no session token, refresh token, remember-me or stored login, and no session table: closing the application ends it, and the next start always shows the sign-in window.

| Topic | Behaviour |
|---|---|
| **Inactivity lock** | After `Authentication:InactivityLockMinutes` (default **15**, allowed **1-120**; 0 or any other value stops start-up, so it cannot be turned off) without keyboard, mouse, touch or stylus input anywhere in the application. Measured with a **monotonic clock** (`IMonotonicClock`, `Stopwatch`), so changing the Windows date or time can neither bypass nor trigger it; a one-second timer compares the idle time with the limit; the boundary is exact (idle time >= limit locks). |
| **Manual lock** | The status-bar button "قفل (Ctrl+L)" and `Ctrl+L`; the same `UserSession.LockAsync` as the inactivity lock. |
| **Windows events** | `SystemEvents.SessionSwitch`: *session lock* locks (reason `WindowsLock`); *console disconnect* and *remote disconnect* lock (reason `UserSessionSwitch`). Unlock, connect and logon events do nothing: **resuming Windows never unlocks the application**. |
| **Lock screen** | An opaque overlay replaces the window content (which is hidden and disabled). It shows only the signed-in user's display name, the lock reason and the unlock controls. Unsaved drafts stay in memory; nothing is written to disk for the lock. A running operation finishes; the lock only hides the UI. |
| **Unlock** | The **same user's password**, through `SessionService.UnlockAsync`: exactly one password verification in every outcome, the same failed-attempt counter and lockout as sign-in (a lock is never extended), a generic failure message, hash upgrade after a correct password. The user (status, department, role, must-change flag) is **reloaded from the database** and replaces the cached principal. |
| **Unlock outcomes** | *Succeeded*: session resumes, idle timer restarts. *Failed* (wrong or empty password): stays locked, generic message. *PasswordChangeRequired* (correct password but a new one must be chosen): stays locked and shows the change-password fields (`AccountService.ChangeOwnPasswordAsync`, the password policy of #72) until it succeeds. *SessionEnded*: the account is **deactivated**, **locked out** (possibly by this attempt) or no longer exists - the session ends and the sign-in window appears with a neutral notice. A deactivation found during the mandatory password change also ends the session. |
| **Current authorization** | `CurrentAuthorization.CheckAsync` / `RequireAsync(principal, permission)` reloads the user and applies the default-deny `AuthorizationRules` to the **current** status, department and role (an inactive user, an unknown user, or one who must still choose a password gets nothing). **Every future role-sensitive Application command or write calls it first**; the desktop `Principal` is a UI snapshot and never authorizes anything by itself. `AccountService` already reloads its acting user in the same way. |
| **Sign-out** | The status-bar button "خروج از حساب": unsaved changes are settled first, the event is audited, the main window and its view models are dropped and a **new** sign-in window with a new view model is shown. |
| **Unsaved work** | Forms register with `UnsavedWorkRegistry` (`IUnsavedWork`: `HasUnsavedChanges`, `CanSave`, `SaveAsync`, `Discard`). **Save** is offered only when every form with changes can save; otherwise the choices are Discard and Cancel. Cancel leaves the user in the session; a failed save does too. No form exists yet, so no prompt can appear in this build; the contract and the prompts (`MessageBoxPrompts`) are tested with fakes. |
| **Switch user** | "ورود با کاربر دیگر" on the lock screen. With unsaved drafts an explicit confirmation (a generic text, no data) is required before they are discarded; declining leaves the session locked. Without drafts there is nothing to discard and nothing is asked. Audited as `SessionUserSwitched`. |
| **Closing** | Closing the window settles unsaved changes like a sign-out (from a locked session: the discard confirmation when there are drafts), ends the session and writes `ApplicationSessionEnded` **best-effort** (waits at most 3 seconds; a failure is counted, never blocks closing). Windows logoff or shutdown ends the session without questions and audits the same way. |
| **Audit** | `SessionSignedOut`, `SessionLocked` (reason in the summary: Inactivity, Manual, WindowsLock, UserSessionSwitch), `SessionUnlocked`, `SessionUserSwitched`, `ApplicationSessionEnded`; failed unlocks are `LoginFailed` (and `AccountLocked`) exactly like sign-in. No password, hash or token is ever written. **A crash, forced termination or power loss writes nothing, and nothing is written afterwards to pretend otherwise.** If the database cannot be reached the lock, sign-out or shutdown still happens; the missing entry is counted in `UserSession.AuditFailures`. |

Known limits (demo): a wrong *current* password during the mandatory password change counts as a failed sign-in like everywhere else, but the change form cannot tell that it locked the account out (the answer is deliberately the same); the user can still use "sign in as another user". Sessions are per process: a second workstation does not see this one's lock or sign-out.

Code: Application `SessionService`, `CurrentAuthorization`, `SessionLockReason`, `UnlockResult`; Desktop `Session/UserSession`, `UnsavedWorkRegistry`, `WindowsSessionMonitor`, `MessageBoxPrompts`, `Views/LockOverlay`, `MainWindow` (input hook, idle timer, shortcut, buttons) and `App` (composition, sign-out and shutdown handling).

## 9. Actor references, the demonstration accounts and the old placeholder (Decision Log #70)

**Creators and approvers are foreign keys.** `Customers`, `Requests` and `RequestItems` (`CreatedByUserId`) and `QcApprovals` (`ApprovedByUserId`) refer to `Users` with deletion restricted; the columns are required. In the authentication schema, `AuditLog.UserId` and `SystemSetup.RecoveryTargetUserId` are foreign keys too, **nullable only because the requirements genuinely allow an unknown actor** (an unknown login name or an operator in an audit entry; no recovery pending). No sentinel user id and no fake account is used anywhere.

**The demonstration accounts (T6)** are created only through the production use cases: the first administrator comes from the real bootstrap path (a token issued and redeemed inside the process, never printed or stored), and the other eight accounts are created by that administrator through `AccountService`. Each of the nine accounts (`demo.admin` in System/Admin, and one user for Central, Management (Manager), Planning, Drawing, QC, Warehouse and Production, plus `demo.supervisor` in Production for the unit supervisor of the work-session walkthrough) gets a random 20-character password from the operating-system generator, different per account and per provisioning, and each is one-time. The seed attributes the sample records to `demo.central` (customers, requests and items); the seed records no QC approvals since the sample stages were removed (Decision Log #118).

- The passwords are written **only** to a credential file: outside any Git working tree (a path inside one is refused before anything is built), never overwriting a file, created with a protected access list for the current Windows user only (mode 0600 elsewhere), name pattern `*.credentials.txt` ignored by Git. By default the file goes under the user's local application data; `--credentials <file>` chooses another. They are never printed by Provisioning, never logged, and not committed. CI masks them and checks that no command output contains them.
- `demo-init` on an already-initialised demonstration database creates no accounts and no file. `demo-reset` replaces the database with new accounts and new passwords.

**Upgrading an older database (safe migration path).** A database from before sign-in existed (the initial migration only) has records whose creator or approver is a placeholder id that never belonged to a user (for the demonstration data: `1`). Such a database cannot satisfy the new keys without inventing accounts, which is forbidden. Therefore:

1. A database **without** business records is upgraded normally and keeps its migration history.
2. A database **with** such records is **refused before the migration starts** (`ActorReferencesInvalidException`): nothing is deleted, altered or invented - the file is byte-for-byte unchanged and still at the initial migration - and the message states the problem, that nothing was changed, and the way forward.
3. A recognised **demonstration** database (the marker of Decision Log #59) is replaced by the explicit reset: `demo-reset <file> --yes` builds a new, consistent database and replaces the old one; `demo-init` on it is refused with the same pointer to the reset. The desktop application shows the message and does not start.
4. **Any other** database needs a reviewed data repair by its owner; the reset refuses an unmarked database.

## 10. Configuration and start-up (Decision Log #69)

`appsettings.json` (Desktop, committed, non-secret) holds the approved defaults; `appsettings.Local.json` (ignored by Git) may override single settings. An unknown setting name is an error (a misspelt security setting must not silently fall back to a default), and a value below its floor stops the application. The database file is `%LocalAppData%\ERP-MES\erpmes.demo.db` unless the environment variable `ERPMES_DATABASE` names another. **Start-up uses `SqliteDatabase.MigrateExistingAsync`: it applies pending migrations to a database file that already exists and never creates, seeds or resets one**; a missing file is an error. Creating the database is IT's job (Provisioning `migrate`, or `demo-init` for the fictional demonstration database). The production migration policy is undecided.

## 11. Deferred and production-only requirements

| Item | Status |
|---|---|
| Central storage of the timeout for multi-PC deployment; server-side session records (forced sign-out, guaranteed end-of-session audit); an absolute maximum session length; per-role timeouts; session-administration screens | Production-only, not started (#74). The demo policy is in section 8a. |
| Item/stage expected-version support (M8) | **Resolved** by Decision Log #77: queue and QC actions must use `UpdateRowsAsync` with the expected versions of the rows they change. Concurrency is per row, not aggregate-level. |
| Database-level audit protection (permissions or equivalent) | Production requirement (#68). |
| Production migration policy | Undecided (#69). |
| Hash benchmark on the lowest-spec supported PC | `benchmark-hash` exists; the PC is not identified. 600,000 stays a starting value (#42). |
| Unicode normalization of user names | Not decided (#62). |
| SQL Server permission scripts, central storage of the policy for multi-PC deployment | Production design, not started. |
| User-management screens; screens that call the authorization rules | Not built. |
| Customer/request UI, queue and QC actions | Separate work. The Jalali questions Q1-Q6 are decided in #73. |

## 12. Verification

Tests (Domain, Infrastructure, Desktop) run against real SQLite files with the real services: the Domain identity types and the Domain boundary; hasher vectors and parameters; sign-in, the generic failure, equal password work for every failure, lockout that is not extended, shared counters, concurrent failures and hash upgrade; case-insensitive names and the unique key in the database; bootstrap (digest only, wrong/expired/limit, one winner of two parallel claims, re-issue, policy, closed path); recovery (issue rules, pending only while valid, limit, expiry, different new password, one winner of two parallel redemptions, no secret in the audit); account administration (Admin-only, reload of the actor, last administrator including concurrent removals); the full authorization matrix and labels that do not matter; policy floors and the settings file; the credential file; the migration (seed, single setup row, restricted foreign keys, no credential column in `Users`, append-only audit, row versions); the actor foreign keys, the refused and the supported upgrade paths of older databases, and start-up that never creates a database; the demonstration accounts and the repeatable reset; source guards (no credential, no weak literal or backdoor construct, no console output of a token except the issue commands, time only through the clock, the desktop never calling the creating migration entry point). The sign-in view model is tested without a window. CI also runs the real Provisioning commands on temporary files.

### Manual check of the sign-in window (checklist section B) - pending your run

The window itself has **not** been checked visually; I cannot run WPF here and CI publishes the application only after a merge to `main` (unchanged by this PR). Local instructions (Windows 10/11, .NET SDK 10.0.401 installed, a clone of this branch, PowerShell):

```powershell
# from the repository root
dotnet --version                                   # must print 10.0.401
dotnet build ERP-MES.sln -c Release

# 1. a fictional demonstration database; one-time passwords go to a protected file outside the repository, not to the screen
$db   = "$env:LOCALAPPDATA\ERP-MES\erpmes.demo.db"
$cred = "$env:LOCALAPPDATA\ERP-MES\demo-credentials\first-run.credentials.txt"
dotnet run --project src/ERP.MES.Provisioning -c Release --no-build -- demo-init $db --credentials $cred
notepad $cred                                      # user<TAB>password per line; close it afterwards

# 2. start the application on that database
$env:ERPMES_DATABASE = $db
dotnet run --project src/ERP.MES.Desktop -c Release --no-build
```

Then work through `docs/MANUAL_UI_CHECKLIST.md` section B. For B7-B9 use a second file: `migrate --database <file2>` (no administrator: B7), `issue-admin-token --database <file2>` (B8; the token is printed once in that console), and after the administrator exists `issue-recovery-token --database <file2> --username <admin> --reason "check"` (B9). To start over: `demo-reset $db --yes --credentials <new file>` (a new credential file name each time). If the application cannot start because the database is from before sign-in, it says so and names `demo-reset`.
