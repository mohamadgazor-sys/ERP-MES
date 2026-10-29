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
- Interactive business actions in the window: creating a request, dispatching a stage, pausing, finishing, QC inspection, costing, dispatch. The services behind them are tested; the buttons are not clicked by the script.
- Session lock, idle timeout and Windows logoff behaviour.
- Anything on a physical Windows 10 machine (the runner is Windows Server 2025).

A person with a Windows machine can reproduce the automated part exactly: `demo-init <file> --credentials <file outside the repository>`, set `ERPMES_DATABASE`, start `ERP.MES.Desktop.exe`, sign in as any `demo.*` account with its one-time password, replace it, and open each navigation entry.
