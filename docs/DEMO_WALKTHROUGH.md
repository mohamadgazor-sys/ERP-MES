# End-to-End Demonstration Walkthrough

| | |
|---|---|
| **Status** | Implemented and covered by the automated end-to-end acceptance test `EndToEndDemoTests` (Decision Log #108, updated for work sessions by #112-#115). The application windows are opened and navigated by an automated UI test on a Windows runner (`docs/UI_SMOKE.md`: sign-in, one-time password replacement and all 23 pages for three accounts), but a person has not yet walked the business steps through the Desktop application on Windows; the manual checklist sections O to Y in `docs/MANUAL_UI_CHECKLIST.md` list what to look at on each screen. |
| **Database** | A demonstration database created with `demo-init` (see `docs/DEMO_SEED_AND_RESET.md`). It contains three sample requests that precede Management's decision (`DEMO-1405-014` waiting for Management, `DEMO-1405-015` just created without a PDF, `DEMO-1405-016` with a nested assembly waiting for Management) and the master data below; the ten demonstration accounts each have a random one-time password in the protected credentials file and must replace it at the first sign-in. |
| **Everything is fictional.** | Names end with `(نمونه آزمایشی)`. Currency is Iranian Rial. Dates are Jalali. |

## 1. What the demonstration database already contains

| Kind | Data |
|---|---|
| Production units | `MILLING` (فرزکاری), `TURNING` (تراشکاری) |
| Machines and rates | `FCC-06` in MILLING at 1,200,000 Rial/hour and `TCC-12` in TURNING at 900,000 Rial/hour, both from 1405/01/01 |
| Shift | `D` 08:00-17:00 with a break 12:00-13:00; both units run it with capacity 1 to 3. The regular week is Saturday-Thursday (Friday off) |
| Material | `ST37-R30` at 90,000 Rial/kg from 1405/01/01 |
| Operator | `OP-DEMO`, linked to the `demo.production` account; that account is designated to MILLING; the operator is assigned to and qualified on `FCC-06` |
| Supervisor | `demo.supervisor` (a Production account), designated to MILLING and marked supervisor - a demonstration choice; who supervises a real unit is the owner's data |
| Left empty on purpose (owner decisions) | overtime limits (Planning must configure them before any overtime can be approved), the worker schedule (Planning schedules the day; otherwise the supervisor must record a reason for working outside the shift) |

## 2. The walkthrough

| Step | Who signs in | Screen | What happens |
|---|---|---|---|
| 1 | demo.secretary | درخواست‌ها | Create a customer and a MAKE request with the customer's request number, attach the request PDF, submit to Management |
| 2 | demo.management | درخواست‌ها | Approve the request |
| 3 | demo.planning, demo.drawing | درخواست‌ها | Planning forwards to Drawing; Drawing records the technical result (fully accepted) |
| 4 | demo.planning, demo.management | قراردادها | Planning decides the contract is required, saves its number and date, attaches the PDF and submits; Management approves |
| 5 | demo.planning | تعریف آیتم‌ها و مرحله‌ها | Define the item (piece, material `ST37-R30`, 1 kg per piece, quantity 10) and one stage in MILLING; release the first stage |
| 6 | demo.planning | تقویم و شیفت‌ها | Schedule `OP-DEMO` for the working day on shift D |
| 7 | demo.supervisor | نوبت‌های کاری | The released stage is in the MILLING list with "قابل اجرا"; choose `FCC-06` (only machines of the unit are offered) and `OP-DEMO`; "تخصیص و شروع". The stage becomes "در حال انجام". `demo.production` (the operator) cannot dispatch |
| 8 | demo.production | نوبت‌های کاری | "نوبت‌های من" shows the session; the operator pauses it with the reason "نبود مواد" and resumes it half an hour later. The stage is NOT paused. Submitting the stage to QC is refused while the session is open |
| 9 | demo.supervisor, demo.planning | نوبت‌های کاری | The supervisor finishes the session (the stage is still not complete); "محاسبهٔ معیارها" shows 3.5 machine-hours, 3.5 labour-hours, 4 hours elapsed, 30 minutes paused by reason; Planning approves the session's actual time (never the person who dispatched it) |
| 10 | demo.production, demo.qc | نوبت‌های کاری, بازرسی کنترل کیفیت | Submit the stage for QC; QC accepts all ten base units |
| 11 | demo.warehouse | دریافت انبار و تکمیل | Record the final receipt |
| 12 | demo.warehouse | هزینه و قیمت | Calculate the cost from the approved session: machining 210 min x 1,200,000/h = 4,200,000 (the pause is excluded; machine time is counted once; no labour cost is added); material 10 x 1 kg x 90,000 = 900,000; total 5,100,000 Rial. The item belongs to an approved contract, so the cost is information and the price comes from the contract; submit the price to Management |
| 13 | demo.management | هزینه و قیمت | Record the contract amount (for example 8,000,000 Rial) with the contract number |
| 14 | demo.warehouse | دریافت انبار و تکمیل | Mark the item finished/sent (refused until the price is approved); record the customer dispatch (date and document reference) |
| 15 | demo.management | شاخص‌های واحدها | MILLING shows 3.5 productive (labour) hours from the approved session, 10 accepted base units, acceptance 100%, one completed stage, no report waiting for approval |

Per-item contract decisions (Decision Log #111): on the contracts screen Planning can release one item of a request from the contract with a reason; that item is then no longer held by the contract. Not in the walkthrough but available: overtime (Planning configures the limits; a unit supervisor must first be designated by Planning), the planning calendar and worker schedule, the Excel item import, raw-material inquiries and Drawing clarifications. Each is covered by its own automated tests and checklist section.

## 2a. Reproducible demonstration path

```
dotnet run --project src/ERP.MES.Provisioning -- demo-init  <file> --credentials <credential-file>
```
Start the Desktop application against `<file>`, sign in as each account in the order of the table (the one-time password is in the credential file and must be replaced at the first sign-in), and follow the steps. The automated `EndToEndDemoTests.One_contract_request_runs_from_the_customer_to_the_dispatch_and_the_metrics_on_the_seeded_demonstration_database` performs exactly these use cases under the real authorization on a fresh demonstration database.

## 3. Honest limits

- The three sample requests (`DEMO-1405-014`, `DEMO-1405-015`, `DEMO-1405-016`) precede Management's decision and carry no stages (Decision Log #118); the walkthrough request does not use them. They are useful to show the list screens and the "attach the PDF before submitting" rule.
- A person has not yet performed this walkthrough in the WPF application on Windows; the automated test performs the same use cases. The work-board window (`WorkBoardView`) in particular has been compiled and its view model tested against the real services, but its rendering and button behaviour have not been seen on a Windows desktop (checklist section Y).
- The worker schedule is not seeded: without step 6, dispatch is refused until the supervisor writes the reason for working outside the shift, which is the intended behaviour.
- The legacy daily job reports still exist for stages that are tracked by reports; a stage is tracked by sessions or by reports, never both (Decision Log #113).
