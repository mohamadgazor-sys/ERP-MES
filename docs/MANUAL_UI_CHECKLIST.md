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
| A7 | Placeholder pages | "کنترل کیفیت" says it is **not implemented yet in this build** ("مشتریان", "درخواست‌ها" and "صف تولید" are real pages, sections D and E) | |
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
| B6 | Choose your own password (6+ characters, no white space; a password containing a space, tab or line break is refused with a message) | The main window opens; the status bar shows the user name; signing in again needs the new password | |
| B7 | A database made with `migrate` only (no administrator) | The window says the system has not been set up; no input fields | |
| B8 | `issue-admin-token`, then start | The first-run screen (token, administrator name, display name, password twice); a wrong token is refused; the right one creates the administrator and returns to sign-in; the first-run screen never appears again | |
| B9 | `issue-recovery-token` for an administrator, then start | "Use recovery token" appears on the sign-in window only now; a new password different from the old one is required | |
| B10 | Passwords on screen | No password is ever shown in clear; the fields are cleared after each action | |

## C. Session lifecycle (session pull request, Decision Log #74) - pending the owner's run

Set `"InactivityLockMinutes": 1` in `appsettings.Local.json` next to the executable to try the inactivity lock quickly (the range is 1-120; 0 is refused at start-up with a message).

| # | Step | Expected | OK |
|---|---|---|---|
| C1 | Sign in, click "قفل (Ctrl+L)" in the status bar; then try `Ctrl+L` | The window content disappears behind the lock screen showing only your name and "قفل شد: درخواست کاربر" | |
| C2 | Type a wrong password on the lock screen | "گذرواژه نادرست است."; the lock screen stays; the field is cleared | |
| C3 | Type your own password | The window content returns where you left it | |
| C4 | Do nothing for the configured time (1 minute) | The lock screen appears by itself ("مدتی از برنامه استفاده نشد") | |
| C5 | Press Win+L, then unlock Windows | The application is locked when you return, and stays locked until you type the password | |
| C6 | Five wrong passwords on the lock screen | The sign-in window appears ("نشست پایان یافت. دوباره وارد شوید."); the correct password is refused for about five minutes (same as B4) | |
| C7 | Lock, then click "ورود با کاربر دیگر" | The sign-in window appears (no questions: there are no forms with unsaved changes in this build) | |
| C8 | Click "خروج از حساب" | The sign-in window appears; the previous user's name is gone | |
| C9 | Close the main window | The application ends; starting it again shows the sign-in window | |
| C10 | Sign in as a non-administrator, lock; in a second copy of the application on the same database sign in as the administrator and deactivate that user (user administration has no screen yet: use the same sequence of steps as the tests, or skip this step); back in the first copy type the password | The session ends and the sign-in window appears | |

The unsaved-changes questions (Save / Discard / Cancel, the switch-user confirmation) cannot be seen in this build because no form can hold unsaved changes yet; they are covered by tests with fake forms.

## D. Customers and requests (customer/request pull request, Decision Log #75) - pending the owner's run

Prepare a fictional demonstration database with `demo-init` (see `docs/AUTHENTICATION.md` section 12); the credential file lists one user per department (`demo.central`, `demo.planning`, `demo.drawing`, `demo.qc`, `demo.management`, ...). Sign in as each user in turn.

| # | Step | Expected | OK |
|---|---|---|---|
| D1 | Open "مشتریان" as `demo.central` | The fictional customers are listed (name, status, last change in Jalali date and time with Persian digits); the add/rename/deactivate buttons are enabled | |
| D2 | Add a customer with an empty or blank name | "نام مشتری را وارد کنید."; nothing is added | |
| D3 | Add a customer "مشتری آزمایشی ۱"; close and restart the application; sign in again and open the page | The customer is still listed | |
| D4 | Select it, type a new name, "تغییر نام انتخاب‌شده" | The name changes and persists after a restart | |
| D5 | Select it, "غیرفعال کردن" | It disappears from the list; tick "نمایش مشتریان غیرفعال" and it shows as "غیرفعال"; "فعال کردن" brings it back; there is no delete button | |
| D6 | Sign in as `demo.qc` or `demo.drawing` and open "مشتریان" | The list is shown; the buttons are disabled | |
| D7 | Sign in as `demo.central`, open "درخواست‌ها" | The seeded requests are listed newest first with request number, customer, Jalali date, Jalali year, status and item count | |
| D8 | Expand "ثبت درخواست جدید": choose a customer, request number `wsj-898`, date `۱۴۰۵/۰۷/۲۰`, a title; "ثبت درخواست" | The request appears with status "ایجادشده"; it is selected below | |
| D9 | Enter the same number again for the same customer and year | "درخواستی با همین شماره ... وجود دارد."; nothing is added. Enter `WSJ-898` instead: it is added and a non-blocking warning names `wsj-898` | |
| D10 | Enter wrong dates: `۱۴۰۵-۰۷-۲۰`, `۱۴۰۴/۱۲/۳۰`, `۱۴۰۵/۱۳/۰۱`, or leave it empty | A specific Persian message each time; nothing is added | |
| D11 | As `demo.central`, "ارسال به مدیریت" on the new request | Status becomes "ارسال‌شده به مدیریت"; the button is disabled afterwards. There is no approve/reject action (not approved yet) | |
| D12 | As `demo.central`, look for "افزودن آیتم" | It is disabled: Central does not define items | |
| D13 | As `demo.drawing`, open the new request, "افزودن آیتم": item `1.10`, part name, quantity `۱۰`, type "ساخت" | The item is listed with "ساخت", quantity `۱۰`, priority "عادی". Trying type "تعمیر" or a priority other than "عادی" is refused with "شما اجازهٔ انجام این کار را ندارید." | |
| D14 | As `demo.planning`, add item `2.10`, quantity `۲.۵`, type "تعمیر", priority "فوری" | The request now shows one MAKE and one REPAIR item; priority labels are exactly عادی / فوری / خیلی فوری | |
| D15 | Enter a duplicate item number, quantity `0` or letters | A specific Persian message; nothing is added | |
| D16 | Open the same request in two copies of the application (two users), add an item in the first, then in the second | The second is told the record was changed by another user, the list reloads and shows the first item; adding again then works | |
| D17 | Lock the session (Ctrl+L) while a request is open, unlock | The same page and selection return (session lifecycle, section C) | |

| D18 | In two copies of the application: copy 1 as `demo.central` on "درخواست‌ها" with a customer selected in the form; copy 2 as `demo.central` or `demo.planning` deactivates that customer ("مشتریان"). In copy 1 press "ثبت درخواست" | "این مشتری غیرفعال است ... درخواستی ثبت نشد ..."; no request is added; the customer list in copy 1 reloads without that customer; the customer is still inactive | |
| D19 | An old request of that now inactive customer | It is still listed and opened; Drawing/Planning can still add items and Central can still submit it | |

Fixed seeded routes and the demonstration banner stay visible; queue stage progression, QC, Excel import and request-detail editing are not part of these screens.

## E. Management production queue (read-only; `docs/PRODUCTION_QUEUE.md`) - pending the owner's run

Prepare a fictional demonstration database with `demo-init` (see `docs/AUTHENTICATION.md` section 12). The seeded requests have routes and several stage states.

| # | Step | Expected | OK |
|---|---|---|---|
| E1 | Sign in as `demo.management`, open "صف تولید" | Heading "صف تولید (فقط‌خواندنی)", a note that nothing on the page changes data, "در حال بارگذاری…" briefly, then a grid of items; the time the data was read is shown with Persian digits | |
| E2 | Look at the layout | Right-to-left; columns read from the right; text is Tahoma; Persian digits in "ردیف", quantities, stage numbers and minutes; request numbers and item numbers are shown exactly as stored (Latin text is not changed) | |
| E3 | Columns | Request number, customer, request status, item number, part name, type (ساخت/تعمیر), quantity, priority (`عادی`/`فوری`/`خیلی فوری`), current stage ("مرحلهٔ x از y: title"), stage status, QC status, two estimate columns | |
| E4 | Order | "خیلی فوری" items first, then "فوری", then "عادی"; within a priority the older request date first; the note under the grid says this order is provisional and that the rule for equal priorities and for overrides is not approved | |
| E5 | Estimates | The estimate headers say "مدت پردازش تخمینی" and "(دقیقه)"; the note says it is not a completion date and ignores calendar, capacity and downtime; no completion date appears anywhere on the page | |
| E6 | Stage and QC columns | An item whose first stage was approved shows stage 2 and "۱ از ۳ مرحله تأیید شده"; an item waiting for QC shows "در انتظار کنترل کیفیت"; an item with every stage approved shows "همهٔ مراحل تکمیل شد" and "—" for the current-stage estimate; an item without a route shows "مسیر تعریف نشده" | |
| E7 | No write controls | The page has only "بارگذاری دوباره": no button, menu or cell edits a priority, a stage, a QC result or the order | |
| E8 | Click "بارگذاری دوباره" | The loading text appears and the grid is replaced by the same rows; the time read is updated | |
| E9 | Sign in as `demo.planning`, `demo.central`, `demo.drawing`, `demo.qc` (one after another) and open "صف تولید" | "شما اجازهٔ مشاهدهٔ صف تولید را ندارید…"; no rows; the same for the administrator account | |
| E10 | Copy 1: `demo.management` on the queue. Copy 2 (administrator, "Provisioning"/test tool): deactivate `demo.management` (no user screen yet; use the method in `docs/AUTHENTICATION.md`). Copy 1: "بارگذاری دوباره" | The permission-denied message replaces the rows | |
| E11 | Add a request and an item in another copy (`demo.central`, `demo.planning`), then "بارگذاری دوباره" as Management | The new item appears in its place in the order (the page is a snapshot until reloaded) | |
| E12 | Delete or rename the database file while the page is open, then "بارگذاری دوباره" | A Persian database-error message; no rows; no crash or exception window; after restoring the file, "بارگذاری دوباره" works again | |
| E13 | An empty database (`demo-init` not run on a fresh `migrate`d file, with a Management user) | "در حال حاضر آیتمی برای نمایش در صف وجود ندارد." (not an error) | |
| E14 | Lock the session (Ctrl+L) on the queue and unlock | The same page returns (section C) | |

