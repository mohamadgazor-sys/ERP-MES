# Offline Demo Guide (Windows 11, no internet needed)

| | |
|---|---|
| **For** | Presenting the ERP/MES demonstration on a Windows computer that is **not connected to the internet** (or to any company network). Written for a person who is not a developer. |
| **What you get** | One ZIP file. Extract it, double-click `Start-Demo.cmd`. The program, the .NET runtime it needs, a small local database (SQLite) with **fictional** data and the launcher are all inside. Nothing is downloaded, installed or configured. |
| **What it is not** | It is not the production system. It never connects to SQL Server, the company server or the internet. The data is fictional and lives in one file inside the package folder. |
| **Status of the evidence** | See section 12 for exactly what was tested and what was not. Read it before the presentation. |

## 1. Which Windows version to use (read this first)

* Target computer: **64-bit (x64) Windows 11**. The package is x64 only. (A Windows 11 ARM computer can run it through the built-in x64 emulation; that combination was not tested.)
* **Recommended: Windows 11 version 24H2 (build 26100) or 25H2.** Microsoft's .NET 10 supported-OS list (checked 2026-10-29: `github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md`, "Last Updated 2026/09/28") names Windows 11 **26H1, 25H2, 24H2** and, only for Enterprise/Education editions, **23H2**. It does **not** list **Windows 11 22H2**, and it says OS versions that are out of support by Microsoft "are not tested or supported by .NET".
* **Windows 11 22H2 is therefore not an officially supported target for this application (.NET 10).** It may run - the application uses ordinary desktop features - but nobody can promise it, and it was **not tested on a real 22H2 machine** (see section 12). If the presentation computer is on 22H2 (build 22621), **upgrade it to 24H2 or 25H2 before the presentation** (Settings > Windows Update; needs internet and time, so do it days earlier, and run `Check-Demo.cmd` afterwards). Do not "fix" this by downgrading the application.
* To see your version: press `Win + R`, type `winver`, Enter. Write down the version and build number. `Check-Demo.cmd` prints the same and tells you whether it is a listed version.

## 2. Copy and extract

1. Copy `ERP-MES-OfflineDemo-<version>-win-x64.zip` and `...zip.sha256` to the presentation computer (USB stick is fine).
2. Optional integrity check: in PowerShell run `(Get-FileHash .\ERP-MES-OfflineDemo-<version>-win-x64.zip -Algorithm SHA256).Hash` and compare with the text in the `.sha256` file (case does not matter).
3. Right-click the ZIP > **Extract All...** to a **local, short folder you own**, for example `C:\ERP-MES-Demo`. Do **not** extract into `Program Files`, onto a network drive, or into a synced cloud folder (OneDrive can lock the database file).
4. Always extract the **whole** ZIP. Copying single files out of it breaks the package.

## 3. Start the demo

1. Double-click **`Start-Demo.cmd`**. A black window runs a quick check (section 10), creates the local demonstration database the first time (20-60 seconds, once), and then opens the application.
2. If Windows shows "Windows protected your PC" (SmartScreen) or a similar message for a file downloaded from the internet: the launcher already removes the "downloaded" mark from the package files (`Unblock-File`), but if the first click is blocked, right-click the ZIP before extracting > **Properties** > tick **Unblock** > OK, then extract again.
3. Sign in (section 4). The window title is "سامانه ERP/MES — نسخه نمایشی" and a yellow banner says it is a demonstration with fictional data.

## 4. Accounts (all fictional)

Open `DEMO-ACCOUNTS.txt` in the package folder. All ten accounts use the **same demo-only passphrase** written there (`PASSPHRASE:` line); user names are lower case.

| User name | Role | Use it to show |
|---|---|---|
| `demo.secretary` | Central | create a customer request, attach its PDF, submit to Management |
| `demo.management` | Management | approve the request, the contract and the price; unit metrics |
| `demo.planning` | Planning | send to Drawing, per-item contract decision, define items and stages, release, schedule the operator |
| `demo.drawing` | Drawing | record the technical result |
| `demo.qc` | QC | inspect a submitted stage |
| `demo.warehouse` | Warehouse | receipt, costing, finished/sent, dispatch |
| `demo.production` | Operator | pause / resume own work session |
| `demo.supervisor` | Unit supervisor (milling) | dispatch, start and finish work sessions on the work board |
| `demo.admin` | System administrator | user administration only |

The order of a full run (create request > approval > Drawing > contract > item and stages > release > work session > QC > costing > price > dispatch > metrics) is the table in `DEMO_WALKTHROUGH.md` (also in the package folder). Sign out ("خروج از حساب", bottom of the window) and sign in as the next role at each step. Two sample requests already exist: `DEMO-1405-014` (waiting for Management) and `DEMO-1405-015` (just created, no PDF yet).

The application locks itself after 15 minutes without activity (Ctrl+L locks it at once); unlock with the same passphrase.

## 5. Reset the demo before the presentation (and between rehearsals)

* Close the application, then double-click **`Reset-Demo.cmd`**, type `YES`. It replaces the contents of **`data\erpmes.demo.db`** with the fictional starting data (20-60 seconds). That one file is all it touches: it refuses any file that is not a marked demonstration database, it needs the application to be closed, and it keeps the old database if anything fails. It does not delete the program, your other files or the logs.
* A reset is **not** the same as deleting the package: to remove everything, delete the extracted folder.
* After a reset the starting state is exactly: 3 fictional customers, 2 sample requests, the master data (milling and turning units, machines FCC-06 and TCC-12 with rates, shift D, material ST37-R30, operator OP-DEMO), ten accounts, no work sessions.

## 6. How to check that it is running on local demo data

* The title bar and the yellow banner say it is a demonstration build with fictional data; every customer ends with "(نمونه آزمایشی)" and request numbers start with `DEMO-`.
* The launcher prints `Local database: ...\data\erpmes.demo.db`; the first line of today's `logs\erpmes-<date>.log` repeats it ("database ...").
* The package contains no SQL Server component and no connection string; the application can only open the SQLite file named by the launcher. You can switch Wi-Fi/Ethernet off completely (or enable airplane mode) before starting; nothing needs to change.

## 7. Where things are stored

| What | Where (inside the extracted folder) |
|---|---|
| Local database | `data\erpmes.demo.db` (plus `-wal`/`-shm` helper files while the program runs) |
| Application log | `logs\erpmes-<yyyymmdd>.log` (start facts, unexpected errors; never passwords or typed data) |
| Launcher log | `logs\launcher-<yyyymmdd>.log` (preflight results, database creation) |
| Settings | `app\appsettings.json` (password and lock rules; do not edit) |
| Manifest / checksums | `MANIFEST.txt`, `FILES.sha256` |

## 8. Prerequisites: what is bundled, what Windows provides, what you do not need

* **Bundled:** the .NET 10 runtime including its WPF desktop libraries (self-contained), all libraries, the native SQLite library (`e_sqlite3.dll`). The exact list is `MANIFEST.txt`.
* **Provided by Windows 11:** PowerShell 5.1 (launcher), the graphics stack, the Tahoma font (Persian text; falls back to Segoe UI/Arial), the time zone "Iran Standard Time" (the application needs it for Jalali dates).
* **Not needed:** internet, NuGet, a separately installed .NET runtime, SQL Server, WebView2, CNC/factory drivers, a network adapter, an administrator account. The application does not use WebView2 or any browser component.
* **Visual C++ redistributable:** see `MANIFEST.txt` and section 12 for the evidence gathered from the build (imports of the native files were inspected). If the preflight or the first start ever reports a missing `VCRUNTIME140.dll` / `MSVCP140.dll`, the official offline installer is "Microsoft Visual C++ Redistributable for Visual Studio 2015-2022, x64" (`vc_redist.x64.exe`) from Microsoft; it needs administrator rights and cannot be included in this package automatically. Windows 11 normally already has it.

## 9. If something goes wrong

| Symptom | What to do |
|---|---|
| Black window says FAIL on a check | Read the FAIL line; it says what to fix (wrong folder, not writable, missing file, little disk space). Fix it and start again. |
| "The package is on a network path" / "cannot write" | Move the extracted folder to `C:\ERP-MES-Demo` (a folder you own). |
| Windows blocks `ERP.MES.Desktop.exe` (SmartScreen / "blocked by your organization") | Unblock the ZIP before extracting (section 3). If a company security tool (AppLocker, antivirus) blocks it, that is a policy decision: ask IT **in advance** to allow the extracted folder. |
| The window opens but sign-in says the user or passphrase is wrong | Names are lower case. Passphrase is in `DEMO-ACCOUNTS.txt` (case-sensitive). After 5 wrong attempts an account locks for 5 minutes; wait, or run `Reset-Demo.cmd`. |
| Database error at start ("پایگاه داده پیدا نشد" / could not run) | Run `Reset-Demo.cmd` (it recreates the demo database). If it still fails, run `Collect-Diagnostics.cmd` and send the ZIP. |
| Text looks cut off or too big | Set display scaling to 100-150 % and the resolution to at least 1280x720 (1920x1080 recommended), then restart the application. |
| Persian letters look wrong | Check that the font Tahoma exists in `C:\Windows\Fonts`; `Check-Demo.cmd` tells you. |
| The application closes by itself | Look at today's `logs\erpmes-*.log` (last lines) and run `Collect-Diagnostics.cmd`. |

## 10. What the launcher checks (`Check-Demo.cmd` runs only these checks)

Windows version and 64-bit, the folder is local and writable, the package files are present, free space of at least 500 MB, the time zone and Tahoma font, screen resolution and display scaling, a leftover `appsettings.Local.json`, and whether the application is already running. FAIL lines stop the start; WARN lines are advice and the demo still starts.

## 11. Diagnostics without exposing secrets

Double-click **`Collect-Diagnostics.cmd`**. It writes `erpmes-diagnostics-<date>.zip` to your Desktop with the logs, the Windows version and the preflight result. It never includes the database, `DEMO-ACCOUNTS.txt` or any password. Application logs record error types and messages only.

**Needs advance preparation or administrator rights:** upgrading Windows to 24H2/25H2 (Windows Update; administrator may be required), allowing the folder in company antivirus/AppLocker, and - only if ever needed - installing the Visual C++ redistributable. Everything else works as a normal user.

## 12. What was verified, and what was not

(From the CI runs of the pull request that added this guide; see `docs/SESSION_LOG.md` for the dated entry.)

**Verified automatically** (workflow `Offline demo package`, jobs on `windows-latest` = Windows Server 2025 Datacenter 24H2, build 26100, and on `windows-2022` = Windows Server 2022 Datacenter 21H2, build 20348; both green; the lines are in the job logs of the pull request that added this guide):

| What | Result |
|---|---|
| Package size | 653 files, 239.3 MB unpacked, **105.8 MB ZIP**; the ZIP's SHA-256 equals its `.sha256` file; every file equals `FILES.sha256` after extraction into a new folder |
| Contents | `app\` (self-contained .NET 10 + WPF + `e_sqlite3.dll`), `tools\provisioning\`, launcher `.cmd`/`scripts\`, `DEMO-ACCOUNTS.txt`, guide, walkthrough, `MANIFEST.txt`, `FILES.sha256`; **no** SQL Server provider, no `appsettings.Local.json`, no database |
| Prerequisites | the import tables of `e_sqlite3.dll`, `coreclr.dll`, `clrjit.dll`, `hostfxr.dll` and the WPF native files were listed with `dumpbin`: only Windows system libraries and the Windows C runtime (`api-ms-win-crt-*`); **no** `VCRUNTIME140.dll`/`MSVCP140.dll` import; the runtime bundles its own `vcruntime140_cor3.dll`. So no Visual C++ redistributable is required **on these machines** (a machine with an unusual C runtime state was not tested) |
| No .NET on the machine | the test renames `C:\Program Files\dotnet`, clears `DOTNET_*` and `PATH`, deletes the NuGet caches, and shows that `dotnet` cannot be resolved - then everything below runs |
| No network for the package | Windows Firewall blocks inbound and outbound traffic of the package programs; a control proves the mechanism works (a blocked copy of `curl` placed in the package folder cannot reach `example.com` while the system `curl` gets HTTP 200); the application holds **0 non-loopback TCP connections and 0 UDP endpoints** in every one of the ~20 sessions of the run |
| Start | `Check-Demo` (no FAIL), `Start-Demo` creates the database and starts the application in about 18-19 s; `demo-status` says `Demo` |
| Accounts | all ten accounts sign in through the real sign-in window with the demo passphrase; no change-password panel appears |
| Pages | `demo.supervisor`, `demo.planning`, `demo.secretary` open all 23 navigation pages (69 visits) with their headings; customers grid 3 rows, requests grid 2 rows |
| Reset | `Reset-Demo -Yes` succeeds, the file is still a marked demonstration database, the accounts sign in again |
| Logs | the demo passphrase appears in no log file; no one-time credential file is left in `%TEMP%`; the application log records its start facts (`.NET 10.0.12`, Windows 10.0.26100, X64, database path) |
| UI behaviour on real screens | see `docs/UI_SMOKE.md` sections 5 and 6: item-scoped contract decisions, release gating, a work session with pause/resume, QC refused while a session is open, and the three visual defects that were found and fixed |

**NOT verified (read these):**

1. **Windows 11 22H2 and every other Windows 11 client build.** GitHub's runners are Windows Server editions (2025 and 2022), not Windows 11 client. Server 2025 shares the 24H2 (26100) code base, which is the recommended target, so the closest evidence is for 24H2; the Server 2022 job shows the same package also runs on an older, officially supported kernel. A run on a real **22H2 (build 22621)** machine did **not** happen, and 22H2 is not on Microsoft's .NET 10 supported list (section 1).
2. **A physically disconnected machine.** The runner cannot be unplugged because its own agent needs the network. The evidence is: no .NET on the machine, no network for the package programs (proved effective by a control), no connection held by the application. A person should still switch the network off once on the real computer (checklist section 13).
3. **A computer that never had .NET or Visual Studio installed.** The runner has Visual Studio and the .NET SDK; the test hides the SDK and runtimes but cannot remove everything Windows or Visual Studio installed (for example the C++ redistributable).
4. **Appearance on the presentation screen:** colours, spacing, 125-150 % scaling, projector, a larger display. The screenshots are 1024x768 at 100 % scaling (the window now fits that size).
5. **Session lock** (15 minutes, Ctrl+L, Windows lock): covered by unit tests, not exercised through the window.
6. **Clicking through the whole presentation path in the window.** The interaction test covers request approval, Drawing, item-level contract decisions, item/stage definition, release gating, work-session start/pause/resume and the QC refusal; it does not click the contract PDF attachment, finishing and approving session time, QC inspection, receipt, costing, price approval, dispatch and the metrics page (these are covered at the service level by `EndToEndDemoTests`).
7. **Antivirus / AppLocker / SmartScreen on the presentation computer.** Unknown; see section 9.


## 13. Pre-presentation checklist (do this the day before, on the presentation computer)

- [ ] `winver` shows Windows 11 **24H2 or newer** (22H2 is not a supported .NET 10 target; upgrade first).
- [ ] 64-bit Windows; screen resolution at least 1280x720 (use the projector/monitor you will present on); display scaling 100-150 %.
- [ ] At least 1 GB free on the drive; the folder is local (for example `C:\ERP-MES-Demo`), you can create files in it.
- [ ] `Check-Demo.cmd` shows no FAIL (read the WARN lines).
- [ ] Switch off Wi-Fi/Ethernet, run `Start-Demo.cmd`, sign in as `demo.management` and `demo.planning`, open two or three pages, sign out, close.
- [ ] `Reset-Demo.cmd` then `Start-Demo.cmd` once more: the customers page shows 3 customers and the requests page shows `DEMO-1405-014` and `DEMO-1405-015`.
- [ ] Do one full rehearsal of the steps you will show, then **run `Reset-Demo.cmd` again** so the presentation starts from the known state.
- [ ] Keep a second copy of the ZIP and its `.sha256` on a USB stick.
