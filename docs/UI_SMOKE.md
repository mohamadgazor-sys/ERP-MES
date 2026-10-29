# UI automation smoke test (Windows runner)

| | |
|---|---|
| **Status** | Implemented (`.github/workflows/ui-smoke.yml`, `tools/ui-smoke/ui-smoke.ps1`; Decision Log #119). Passed on the pull-request head that fixed the defect it found (details in section 3). |
| **What it is** | The real, published WPF application is launched on a GitHub `windows-latest` runner against a throw-away demonstration database (created with `demo-init`; the random one-time passwords stay in a temp file outside the repository and are masked in the log). The script signs in through the real sign-in window with Microsoft UI Automation (`System.Windows.Automation`, no third-party package), replaces the one-time password in the real change-password panel, then walks **every entry of the navigation list** and checks that the page opened. |
| **What it is not** | It does not click through the business workflow (dispatch, pause, QC...): that is covered at the service level by `EndToEndDemoTests`. It does not judge appearance. |

## 1. Exactly what it checks

For each of three accounts (`demo.supervisor`, `demo.planning`, `demo.central`):

1. The sign-in window appears and its fields can be filled (`SignInUsername`, `SignInPassword`).
2. Signing in with the one-time password shows the change-password panel (a one-time password must be replaced before use).
3. After the replacement the main window appears with the navigation list. The list is virtualised, so the script scrolls with PageDown until no new row appears. **All 23 entries were seen for each account.**
4. Selecting each entry shows the page's heading (the heading text appears a second time next to the navigation entry itself). 23 pages x 3 accounts = 69 page visits.
5. The numbers of data-grid rows that the seed defines: the customers page shows **3** rows and the requests page **2** rows; the work board has at least one grid.

The log also records, per page, the number of grids, rows and buttons, so the run documents what each screen contained. The script takes a screenshot per page into the runner's temp folder; **those images are not uploaded and nobody has looked at them**, so this document makes no claim about appearance.

## 2. What the run showed (head `e152e96`, first fully green run of the script)

- The three accounts reached the main window and opened all 23 pages each, 69 visits, 0 failures.
- Customers grid 3 rows, requests grid 2 rows (the two seeded sample requests), unit metrics and calendar grids populated as seeded (calendar 10 rows for the accounts allowed to see it, machines 4 rows, operators 3 rows for Planning and the supervisor).
- The work board ("نوبت‌های کاری") opens for all three accounts with 3-4 grids; the unit queue ("صف واحدهای تولید") opens with 2-3 grids after the fix below.

## 3. A real defect found by it

The first complete run failed on exactly one page for all three accounts: selecting "صف واحدهای تولید" left the previous page and title on screen. Cause: `UnitQueueView` added its `BoolToVisibility` converter to its resources **after** `InitializeComponent()`, but its XAML resolves `{StaticResource BoolToVisibility}` while it is parsed, so building the page threw and the navigation silently stayed on the old page. The view-model tests and the end-to-end test cannot see this. Fix: the converter is declared in the XAML like in the other views (commit `e152e96`). The same run now passes.

## 4. What is still NOT verified by anything

- Visual layout and right-to-left rendering, fonts, high-DPI scaling, window resizing (checklist sections A and following).
- Interactive business actions of the SMOKE script (it only opens pages). Part of the business actions is exercised by the separate interaction test of section 5; the rest (creating a request with its PDF through the file dialog, finishing a session, approving time, QC inspection, receipt, costing, price approval, dispatch) is still not clicked by any automation.
- Session lock, idle timeout and Windows logoff behaviour.
- Anything on a physical Windows 10 machine (the runner is Windows Server 2025).

A person with a Windows machine can reproduce the automated part exactly: `demo-init <file> --credentials <file outside the repository>`, set `ERPMES_DATABASE`, start `ERP.MES.Desktop.exe`, sign in as any `demo.*` account with its one-time password, replace it, and open each navigation entry.

## 5. The interaction test (`tools/ui-smoke/ui-interact.ps1`, same workflow, second step)

Added 2026-10-29 (Decision Log #120). It runs against a **second, fresh disposable database** created with `demo-init --presentation` (so the accounts already use the demo passphrase) and uses only visible controls through UI Automation: it clicks buttons, types into text boxes, picks combo-box entries and answers a dialog. It never calls an Application service. Passphrase and database are throw-away values on a CI runner.

What it checks (each line is a `CHECK OK` in the log; the run is one process per section, with a real application restart in the middle):

| # | Check | Evidence in the UI |
|---|---|---|
| 1 | Central sees the two seeded requests; **premature action**: "ارسال به مدیریت" is disabled for `DEMO-1405-015` (no PDF yet); **role**: no approve button for Central | button `IsEnabled`/absence |
| 2 | Management approves `DEMO-1405-014` | the status cell changes from "ارسال‌شده به مدیریت" to "تأییدشده توسط مدیریت" |
| 3 | **Persistence**: the application is closed and started again; Planning sees the approved status; Planning forwards the request to Drawing | status cell |
| 4 | Drawing has no approve button and records the result (fully accepted) | status cell |
| 5 | **Item-scoped contract decision**: Planning decides "contract required" for the request, then "this item does not need the contract" for item 3.20 only: 3.20 shows its own decision and reason, 3.10 is unchanged ("follows the request"); Management cannot make the per-item decision | item grid text, button state |
| 6 | Planning defines a new item `UI.1` (type, material, weight, priority) and a stage in the MILLING unit through the real forms | item and stage grids |
| 7 | **Visible release gating**: the unit-queue page lists the stage with the reason "شرط‌های آزادسازی تولید برقرار نیست"; pressing release does **not** release it (not in the queue); after the item-level decision "no contract needed" the same stage is released and appears in the MILLING queue | grid rows, queue grid |
| 8 | **Unauthorized action**: the operator (`demo.production`) cannot dispatch (button disabled) | button state |
| 9 | **Production session**: the supervisor dispatches `UI.1` to machine `FCC-06` and the unit's operator and starts it: confirmation "نوبت کاری ثبت و شروع شد", the stage shows "در حال اجرا" with one active session | stage row, message |
| 10 | The operator pauses the session with the reason "قطع برق" (a real modal dialog is answered); the session shows "متوقف"; resuming it outside the shift **needs a reason**, which the application now asks for in a prompt (see below); then a QC submission **is refused while the session is open**: the screen shows "the stage is not sent to QC while an assigned, running or paused work session exists" and the stage does not move to "waiting for QC" | session row, stage row, message |

**A real defect found here:** the operator's page had no way to give the out-of-shift reason (the box exists only in the supervisor's dispatch panel), so an operator could not resume a paused session outside shift hours: the rule was enforced, but unanswerable. The window now answers a refused start/resume "reason required" with a prompt and retries (`WorkBoardView`, `WorkBoardViewModel.LastIssue`). The business rule itself is unchanged.

Run result and the exact lines: see the workflow run of the pull request that added it (listed in `docs/SESSION_LOG.md`). The checks are only as strong as what a UI Automation tree exposes: a status that is not shown as text cannot be asserted.

## 6. What looking at real screenshots found (2026-10-29)

The workflow prints a few screenshots, cropped to the application window, into the job log (base64) so that they can be decoded and looked at; they show only fictional data and password boxes are masked. They are taken at the runner's resolution of **1024x768**. Findings and what was done:

| Finding | Cause | Fix |
|---|---|---|
| Right-to-left layout, navigation on the right, Persian text and the Tahoma font render correctly; the yellow demo banner, status bar with Jalali date/time, buttons and grids are readable | - | none needed |
| The main window (designed 1100 px wide) was cut off at the screen edge on a 1024 px screen | fixed width larger than the screen | the window is limited to the work area at start (`MainWindow`) |
| Request numbers such as `DEMO-1405-015` were shown reversed (`015-1405-DEMO`) in the right-to-left grids, and also inside Persian sentences (headings, PDF file name, machine code `FCC-06` shown as `06-FCC`) | bidirectional text reordering of free-form Latin text | grid columns for request, item and contract numbers use a left-to-right cell style (`LtrCell`); view-model sentences wrap such text in a left-to-right **embedding** (`Messages.Ltr`, U+202A...U+202C). A first attempt with Unicode isolates (U+2066...) was visible in a screenshot as ineffective: WPF does not honour isolates |

Not judged by this: whether the layout is pleasing, spacing, colours, the look at 125-150 % scaling (the runner has one resolution and 100 % scaling), the projector, an actual Windows 11 client.

## 7. Session lock

The authentication design (`docs/AUTHENTICATION.md`, Decision Log #74) locks the session after 15 minutes without input, on Ctrl+L, and on Windows session events. This was **not** exercised through the UI (the 15-minute timer would make the job long; Ctrl+L was not sent). The behaviour is covered by the `UserSession` unit tests on Windows (`tests/ERP.MES.Desktop.Tests/UserSessionTests.cs`). A person should press Ctrl+L once on the presentation computer (checklist).
