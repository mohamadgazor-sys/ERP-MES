# Manual UI Checklist (demonstration build)

Approved in Decision Log #50 as the check for the shell until a UI-automation approach is chosen. Windows CI proves the application **builds** and the unit tests pass; it cannot show how the screens look. Run this list on a Windows 10 or 11 machine with a CI-built or locally built application and report what you see (a screenshot per failed item helps).

**Status:** the list covers the shell of PR "Demo shell and navigation". It grows with each screen. Nothing on it has been run by me; I cannot run WPF in my environment.

## A. Shell and navigation (this change)

| # | Check | Expected | Result |
|---|---|---|---|
| A1 | Start the application | One window titled "سامانه ERP/MES — نسخه نمایشی", centred, opens on the dashboard | |
| A2 | Layout direction | Navigation list is on the **right**; text is right-aligned; reading order is right to left | |
| A3 | Font | Persian text uses Tahoma (letters پ چ ژ گ ک ی join correctly; the half-space in "درخواست‌ها" and "محدودیت‌ها" shows as a gap without a visible box) | |
| A4 | Demo banner | Yellow banner at the top says the build is a demonstration with fictional data | |
| A5 | Navigation | Clicking each item changes the page and the heading; the selected item is highlighted | |
| A6 | Dashboard | Four tiles; each shows "—" and says no data has been loaded; no invented numbers | |
| A7 | Placeholder pages | "مشتریان", "درخواست‌ها", "صف تولید", "کنترل کیفیت" each say they are **not implemented yet in this build** | |
| A8 | Limitations page | Lists what the demonstration does not include | |
| A9 | Status bar | Shows the current Jalali date and time with **Persian digits** and 24-hour time, for Iran Standard Time (compare with a trusted Jalali calendar); it updates within about 30 seconds | |
| A10 | Resize | Shrinking to the minimum size and maximising keep all text readable; no overlap | |
| A11 | High DPI | At 125% and 150% scaling the text is sharp and nothing is clipped | |
| A12 | Close | Closing the window ends the process (no background process left in Task Manager) | |

## B. Added by later changes (not yet applicable)

Sign-in, customers, request entry, queue, QC approval, persistence across restarts and the reset command are added to this list by the PRs that build them.

## B. Sign-in (sign-in pull request)

Exact local build and run instructions (Windows, SDK 10.0.401): `docs/AUTHENTICATION.md`, last section. In short: build, run `demo-init <file> --credentials <new file outside the repository>` (the one-time passwords are in that file, not on screen), set the environment variable `ERPMES_DATABASE` to the database and start the application. The application never creates a database. **This section has not been run yet; it waits for your check.**

| # | Check | Expected | Result |
|---|---|---|---|
| B1 | Start without a database file | A message names the missing file and asks to run Provisioning; the application exits | |
| B2 | Start with the prepared database | A small sign-in window (RTL, Tahoma, yellow demonstration banner) with user name and password; no first-run fields | |
| B3 | Wrong password / unknown user | The same Persian message for both; the window stays | |
| B4 | Five wrong passwords for one user | The same generic message every time (the window never says the account is locked); afterwards even the correct password is refused with that same message for about five minutes (Decision Log #65) | |
| B5 | Sign in as `demo.qc` with its one-time password | The "change password" step appears (current, new, repeat); a wrong current password, a short new password or a different repeat is refused with a message | |
| B6 | Choose your own password (6+ characters, no spaces; a password containing a space is refused with a message) | The main window opens; the status bar shows the user name; signing in again needs the new password | |
| B7 | A database made with `migrate` only (no administrator) | The window says the system has not been set up; no input fields | |
| B8 | `issue-admin-token`, then start | The first-run screen (token, administrator name, display name, password twice); a wrong token is refused; the right one creates the administrator and returns to sign-in; the first-run screen never appears again | |
| B9 | `issue-recovery-token` for an administrator, then start | "Use recovery token" appears on the sign-in window only now; a new password different from the old one is required | |
| B10 | Passwords on screen | No password is ever shown in clear; the fields are cleared after each action | |

