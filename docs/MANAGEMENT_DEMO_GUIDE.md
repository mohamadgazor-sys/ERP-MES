# راهنمای نمایش به مدیریت — برنامه جامع مدیریت و کنترل تولید ایران‌تول

| | |
|---|---|
| **Purpose** | A short, practical script for the management demonstration (owner request of 2026-11-13). Everything is fictional (names end with `(نمونه آزمایشی)`); currency is Iranian Rial; dates are Jalali. |
| **Data** | Created by `demo-init` through the real application services under the real demo accounts (`DemoScenarioSeeder`, Decision Log #139): the workflow history, audit trail and permission rules are those of normal operation. |
| **Accounts** | The eleven demo accounts of `DEMO-ACCOUNTS.txt` (same demo passphrase for all). To restart from the beginning: `Reset-Demo.cmd`. |
| **Verified** | The scenario is created and checked by the automated test `DemoScenarioTests`, and the screens named below are walked by the Windows UI-automation scripts of CI. A person must still rehearse it once on the presentation computer. |

## 1. What is already in the database when you start

| Request | Type | State you will see | Items (system numbers) |
|---|---|---|---|
| `DEMO-1405-101` | MAKE | Approved, Drawing accepted, **no contract needed**, **all items released with the single «ارسال همهٔ آیتم‌ها به صف کار» action** | `-001` شفت (آبکاری بیرونی → تراشکاری → کنترل نهایی, 10 pcs, فوری); `-002` بوش (فرزکاری, 20 pcs); `-003` صفحه نگهدارنده (فرزکاری, 5 pcs, خیلی فوری) |
| `DEMO-1405-102` | REPAIR | Approved, Drawing accepted, released | `-001` گلند آب‌بندی (delivery date and warehouse code filled) |
| `DEMO-1405-103` | MAKE | Contract **required**, saved, PDF attached, **submitted — waiting for Management** | `-001` چرخ‌دنده مارپیچ, stage defined, not released |
| Older samples | — | `DEMO-1405-014` (waiting for Management), `-015` (just created), `-016` (nested assembly) | for the live steps below |

What the first request already went through (visible in history and on the screens):

- the **bush** ran on machine FCC-06 with operator OP-DEMO in a work session with a **pause for missing material**, finished, **time approved by Planning**, submitted and **accepted by QC** (route complete);
- the **shaft** has an **external plating stage** (lead time 5 calendar days): the Warehouse dispatched 10 pieces, **7 came back and QC accepted them**, and **3 pieces were recorded as lost at the contractor** (reason recorded). The stage now waits for **Management's shortfall decision**; the lost pieces are shown separately from scrap and the next stage is locked until the decision;
- **downtime** of FCC-06: one accepted maintenance window and one breakdown report waiting for Planning; a **calendar closure** (official holiday) and an **approved overtime assignment** (the limits are example values — the real limits are an owner decision).

## 2. The live demonstration (about 20 minutes)

| # | Account | Screen | Action | What to point out |
|---|---|---|---|---|
| 1 | `demo.management` | درخواست‌ها | Open `DEMO-1405-014` → approve | Rejection needs a reason; approval notifies Planning |
| 2 | `demo.secretary` | مشتریان, درخواست‌ها | Create a customer and a request (the customer's own number, free text) | Duplicate customer/number/year is refused; the PDF must be attached before submitting |
| 3 | `demo.planning` → `demo.drawing` | درخواست‌ها | Forward to Drawing; Drawing accepts | Only Drawing decides technical feasibility |
| 4 | `demo.planning` | قراردادها | Decide «قرارداد لازم نیست» for the new request, or for a single item of a contract request | The decision is **per item**; one item never gates another |
| 5 | `demo.planning` | تعریف آیتم‌ها و مرحله‌ها | Define an item; in **عنوان مرحله** type «فرز»: the list narrows, **Enter** or a click selects, **Tab** accepts a valid title; a title that is not in the catalogue is refused. Choose a unit and save | Only active catalogue titles can be used; retired ones are not offered |
| 6 | `demo.planning` | تعریف آیتم‌ها و مرحله‌ها | Press **ارسال همهٔ آیتم‌ها به صف کار واحدهای تولیدی** → read the question → «خیر» changes nothing; «بله» releases | If one item is blocked nothing is released and the blocking item is named; an assembly waits for its children; a second press is refused |
| 7 | `demo.management` | درخواست‌ها (history) | Open the request history | The release is recorded with the user, department and time |
| 8 | `demo.supervisor` | نوبت‌های کاری | Dispatch a stage (e.g. `DEMO-1405-101-003`) to FCC-06 and OP-DEMO | Missing qualification is a warning; work outside the shift needs a reason |
| 9 | `demo.production` | نوبت‌های کاری | Pause with a reason, resume, finish; submit for QC | The pause is not machine runtime for costing |
| 10 | `demo.qc` | بازرسی کنترل کیفیت | Accept, send to rework or scrap quantities | Only QC inspects; the next stage is locked until QC approves |
| 11 | `demo.warehouse` | کار بیرونی | Open the shaft's external stage: history of dispatch / return / missing | Warehouse alone records; each transaction keeps user and time |
| 12 | `demo.management` | بازرسی کنترل کیفیت | Decide the shaft's **shortfall** (accept it, with a reason) | Lost-at-contractor is separate from scrap; the next stage then receives the 7 accepted units |
| 13 | `demo.maintenance` → `demo.planning` | توقف ماشین‌ها | Report a downtime; Planning accepts the breakdown report | Downtime moves the planned end dates |
| 14 | `demo.productionmanager` → `demo.management` | اضافه‌کاری | Review the overtime assignment | Within the limits the Production Manager or Management approves; beyond them only Management |
| 15 | `demo.management` | قراردادها | Approve the contract of `DEMO-1405-103`; then `demo.planning` releases its item | The contract gate opens only after Management approves |
| 16 | `demo.management` | صف تولید, شاخص‌های واحدها | Show the live queue and unit metrics | The queue reflects Planning's priorities and Management overrides |

Account names: `demo.secretary`, `demo.management`, `demo.planning`, `demo.drawing`, `demo.qc`, `demo.warehouse`, `demo.production`, `demo.supervisor`, `demo.productionmanager`, `demo.maintenance`, `demo.admin`.

## 3. The strongest features to show

1. One request is the container for everything; MAKE and REPAIR are separate requests; the customer's own request number is kept exactly as typed.
2. **Item-scoped contract decisions** and the gates that explain, in plain Persian, why something cannot be released.
3. **One action to release a whole request**, with a confirmation, an atomic result, a full audit and a history entry.
4. **Sequential stages with QC approval** and quantity-based inspection (accept / rework / scrap).
5. **External processing** with partial dispatch/return and **pieces lost at the contractor** handled by a Management decision.
6. Work sessions with pauses, approved time and cost from the real session; machine downtime and overtime.
7. Every action names the person and the time; nothing is silently overwritten.

## 4. Limits to state honestly

Overtime limits and the contract-amount field are owner decisions (`docs/OWNER_REVIEW_REPORT_2026-11-13.md`). A later QC correction that sends an item back to stage 1 is approved but not built yet. The production SQL Server instance is not chosen. The demo runs on SQLite on one computer.
