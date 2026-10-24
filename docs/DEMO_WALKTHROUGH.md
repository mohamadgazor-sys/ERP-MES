# End-to-End Demonstration Walkthrough

| | |
|---|---|
| **Status** | Implemented and covered by the automated end-to-end acceptance test `EndToEndDemoTests` (Decision Log #108). A person has not yet walked it through the Desktop application; the manual checklist sections O to X in `docs/MANUAL_UI_CHECKLIST.md` list what to look at on each screen. |
| **Database** | A demonstration database created with `demo-init` (see `docs/DEMO_SEED_AND_RESET.md`). It contains six sample requests and the master data below; the eight demonstration accounts each have a random one-time password in the protected credentials file and must replace it at the first sign-in. |
| **Everything is fictional.** | Names end with `(نمونه آزمایشی)`. Currency is Iranian Rial. Dates are Jalali. |

## 1. What the demonstration database already contains

| Kind | Data |
|---|---|
| Production units | `MILLING` (فرزکاری), `TURNING` (تراشکاری) |
| Machines and rates | `FCC-06` in MILLING at 1,200,000 Rial/hour and `TCC-12` in TURNING at 900,000 Rial/hour, both from 1405/01/01 |
| Shift | `D` 08:00-17:00 with a break 12:00-13:00; both units run it with capacity 1 to 3. The regular week is Saturday-Thursday (Friday off) |
| Material | `ST37-R30` at 90,000 Rial/kg from 1405/01/01 |
| Operator | `OP-DEMO`, linked to the `demo.production` account; that account is designated to MILLING |
| Left empty on purpose (owner decisions) | overtime limits (Planning must configure them before any overtime can be approved), unit supervisors (Planning reviews reports as the flagged fallback), worker schedule |

## 2. The walkthrough

| Step | Who signs in | Screen | What happens |
|---|---|---|---|
| 1 | demo.central | درخواست‌ها | Create a customer and a MAKE request with the customer's request number, attach the request PDF, submit to Management |
| 2 | demo.management | درخواست‌ها | Approve the request |
| 3 | demo.planning, demo.drawing | درخواست‌ها | Planning forwards to Drawing; Drawing records the technical result (fully accepted) |
| 4 | demo.planning, demo.management | قراردادها | Planning decides the contract is required, saves its number and date, attaches the PDF and submits; Management approves |
| 5 | demo.planning | تعریف آیتم‌ها و مرحله‌ها | Define the item (piece, material `ST37-R30`, 1 kg per piece, quantity 10) and one stage in MILLING; release the first stage |
| 6 | demo.production | صف واحدهای تولید | The released stage is in the MILLING queue; start it |
| 7 | demo.production, demo.planning | گزارش کار روزانه | The operator reports 08:00-12:00 on FCC-06 for the working day and submits; Planning approves as the fallback reviewer (the screen says so) |
| 8 | demo.production, demo.qc | صف واحدهای تولید, بازرسی کنترل کیفیت | Submit the stage for QC; QC accepts all ten base units |
| 9 | demo.warehouse | دریافت انبار و تکمیل | Record the final receipt |
| 10 | demo.warehouse | هزینه و قیمت | Calculate the cost: machining 240 min x 1,200,000/h = 4,800,000; material 10 x 1 kg x 90,000 = 900,000; total 5,700,000 Rial. The item belongs to an approved contract, so the cost is information and the price comes from the contract; submit the price to Management |
| 11 | demo.management | هزینه و قیمت | Record the contract amount (for example 8,000,000 Rial) with the contract number |
| 12 | demo.warehouse | دریافت انبار و تکمیل | Mark the item finished/sent (refused until the price is approved); record the customer dispatch (date and document reference) |
| 13 | demo.management | شاخص‌های واحدها | MILLING shows 4 productive hours, 10 accepted base units, acceptance 100%, one completed stage, no report waiting for approval |

Not in the walkthrough but available: overtime (Planning configures the limits; a unit supervisor must first be designated by Planning), the planning calendar and worker schedule, the Excel item import, raw-material inquiries and Drawing clarifications. Each is covered by its own automated tests and checklist section.

## 3. Honest limits

- The sample requests (`DEMO-...`) still have stages without a production unit; they belong to the earlier demonstration stage flow (Decision Log #79-#81). The new walkthrough request does not use that flow.
- A person has not yet performed this walkthrough in the WPF application on Windows; the automated test performs the same use cases.
