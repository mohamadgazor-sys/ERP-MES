# Management demonstration - final acceptance checklist

**Status of the evidence (read first).** The boxes below are **empty on purpose**: tick one only when a person did it on Windows. The automated workflow `Offline demo package` proves, on **Windows Server runners** (windows-2022, windows-latest), that the package unpacks, verifies its checksums, initialises the demo database, signs in all eleven demo accounts, resets and shows the official name; the `UI smoke` workflow opens every page and clicks the main buttons by role. **That is not the owner's physical test**, which is a separate step on the owner's own computer (Windows 10/11 client, offline). The exact package to use is named in `HANDOFF.md` ("Offline package - CURRENT").

Accounts: `DEMO-ACCOUNTS.txt` in the package (eleven fictional accounts, one demo passphrase). Script of the demonstration: `docs/MANAGEMENT_DEMO_GUIDE.md`. Reset to the start: `Reset-Demo.cmd`.

## A. Preparation (before the audience arrives)
- [ ] The presentation computer is Windows 10 or 11 with a supported .NET 10 runtime (or the self-contained package); Windows 11 22H2 is not on Microsoft's supported list: check `winver`.
- [ ] `Check-Demo` passes; the ZIP checksum equals the `.sha256` and the value recorded in `HANDOFF.md`.
- [ ] Wi-Fi off / cable out; `Start-Demo` opens the application; antivirus does not block it (the package is not code-signed).
- [ ] `Reset-Demo` returns the data to the start.

## B. Branding and language
- [ ] The window title and the header band show the official application name «برنامه جامع مدیریت و کنترل تولید ایران‌تول».
- [ ] The interface is Persian and right-to-left; dates are Jalali with 24-hour time; amounts are in Rial (no Toman anywhere); codes and file names stay left-to-right.
- [ ] No button label ends with an ellipsis; every button has a tooltip that says what it does and what it does not do.

## C. Main business workflow (request to finished item)
- [ ] `demo.secretary` creates a customer and a request with the customer's own number; a duplicate number for the same customer and year is refused; the PDF is required before submission.
- [ ] `demo.management` approves (a rejection needs a reason); `demo.planning` forwards to Drawing; `demo.drawing` records the result.
- [ ] `demo.planning` decides the contract per request and per item; an item with no decision follows the request.
- [ ] `demo.planning` defines an item and its stages (stage title from the catalogue), then releases all items to the production queue with the single release action; a blocked item stops the whole release and is named.
- [ ] The request page shows the item progress, the completion percentage (completed / total) and the corrected / not-current indicator column.

## D. Permissions (try each, expect refusal or no button)
- [ ] Only `demo.qc` inspects and corrects QC; only `demo.drawing` decides technical feasibility; only `demo.planning` creates contracts; only `demo.management` approves requests, contracts and prices; only `demo.warehouse` receives and dispatches; `demo.admin` alone administers users.
- [ ] The Production Manager reopens approved reports and designates supervisors; the supervisor reviews the unit's reports; nobody reviews their own report.
- [ ] `demo.central`, `demo.production`, `demo.supervisor` do not see the Warehouse reversal, report reopening or contract amount actions.

## E. Production release, work sessions and queue
- [ ] `demo.supervisor` dispatches a stage to a machine and operator (missing qualification is a warning; work outside the shift needs a reason).
- [ ] `demo.production` pauses with a reason, resumes, finishes and submits for QC; `demo.planning` approves the session time (never the person who dispatched it).
- [ ] The production queue shows Planning's priority labels exactly («عادی», «فوری», «خیلی فوری»); a Management override is listed with who, when, old and new order, and the reason.

## F. QC
- [ ] `demo.qc` records accepted / rework / scrap quantities; the next stage stays locked until QC approves; scrap creates a shortfall that Management decides.
- [ ] QC correction of an approved stage: partial quantity, mandatory reason, history kept, the item marked corrected, downstream stages marked and not reset.

## G. External processing
- [ ] The shaft's external stage shows dispatch, return and the three pieces lost at the contractor (shown apart from scrap); Management's shortfall decision releases the next stage; lead time appears in the forecast.

## H. Machine downtime and overtime
- [ ] `demo.maintenance` reports downtime; `demo.planning` accepts or rejects; the forecast moves; the history shows who and when.
- [ ] The overtime assignment is reviewed within the limits (the demo limits are examples; real limits are entered by Management before go-live).

## I. Costing, price and Warehouse
- [ ] Cost is calculated from approved time and rates; Management approves the price (a changed price needs a reason); `demo.warehouse` records the receipt, then finished/sent, then optionally a customer dispatch.

## J. Warehouse reversal (live, not seeded)
- [ ] `demo.warehouse` proposes a reversal with a reason; the record stays current; `demo.management` approves (original marked «برگشت‌خورده», still listed) or rejects with a reason; the Warehouse cannot approve; a real new receipt can then be recorded.

## K. Job-report amendment (live, not seeded)
- [ ] `demo.planning` or `demo.productionmanager` reopens an approved report with a reason; the original stays valid and shows «اصلاحیه در جریان است»; the operator corrects, the supervisor approves; the original shows «جایگزین‌شده»; metrics count only the revision; when machine time changed the cost snapshot is shown not current, the Warehouse receipt is not.

## L. Contract amount, search and notice
- [ ] `demo.planning` enters the contract amount; `demo.management` approves; the price screen proposes the amount; changing the price needs a reason; Management cannot type the amount.
- [ ] The contract list is searched by contract number, date, customer and request number.
- [ ] The home page of Planning and Warehouse shows the approved-contract notice.

## M. QC correction after customer dispatch (live, not seeded: `MANAGEMENT_DEMO_GUIDE.md` section 2c)
- [ ] After a dispatch, QC corrects the stage; `demo.management` sees the red warning (dispatch kept and not reversed; downstream records may not be current; a new approval may be needed); cost, price and receipt show not current; recalculation, new Management approval and a real new receipt follow; the dispatch stays listed.

## N. Close
- [ ] `demo.management` closes a finished request manually (blockers are listed per item and a confirmation is asked); the closed request is read-only.
- [ ] `Reset-Demo` works; no credential file, database backup or real customer PDF is left on the computer.

## Sign-off
Tested by: ________  Date (Jalali): ________  Computer / Windows version: ________  Package SHA-256 checked: ☐  Defects found (list): ________
