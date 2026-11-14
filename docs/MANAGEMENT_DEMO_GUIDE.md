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

## 2b. Added after the first package (needs the package built from the main that contains them)

| Where | What to show |
|---|---|
| Home page (صفحهٔ اصلی) | Four live counts: open requests, items in production, items waiting for QC, urgent unfinished items (Decision Log #141) |
| درخواست‌ها -> select a request | The progress block above the item grid: "N از M آیتم تکمیل شده" with active / waiting-blocked / rework counts and a bar; each item shows "k از n مرحله" and its current stage and status |
| تعریف آیتم‌ها و مرحله‌ها, request `DEMO-1405-016` (nested assembly; after Management approval and Drawing) | Define stages for the parent and the children and press the release button: the children are released, the parent shows «منتظر زیرمجموعه‌ها … اپراتور نمی‌تواند آن را شروع کند»; in صف واحدهای تولید it is under «در انتظار آزادسازی» with «آزاد نشده: منتظر پایان مسیر و تأیید کنترل کیفیت همهٔ زیرمجموعه‌ها» (Decision Log #140) |

## 2c. QC correction after customer dispatch (owner decisions Q1-Q8, Decision Log #142/#143)

**State honestly: this scenario is NOT seeded.** The demonstration database has no item that has already been QC-approved, received, priced and dispatched (seeding one would change the offline package and its physical-test record; it is a separate, later change). It is performed **live** with the demonstration accounts, about 15 minutes. The automated end-to-end test `A_QC_correction_after_receipt_price_and_dispatch_...` (`EndToEndDemoTests`) performs the same steps at the service level on the seeded database; **no person has yet clicked through this live sequence** - rehearse it once before showing it. Everything listed exists in the application; the page titles and button texts below are the real ones.

Part 1 - take one new item to "dispatched" (skip it if you rehearsed and kept a request in that state)

| # | Account | Page / button | Action |
|---|---|---|---|
| 1 | `demo.secretary` | درخواست‌ها: «ثبت درخواست», «پیوست یا جایگزینی PDF», «ارسال به مدیریت» | Create request `COR-1` (Make) for any customer, attach a PDF, send to Management |
| 2 | `demo.management` | درخواست‌ها: «تأیید» | Approve |
| 3 | `demo.planning` | درخواست‌ها: «ارسال به نقشه‌کشی» | Forward to Drawing |
| 4 | `demo.drawing` | درخواست‌ها: «ثبت نتیجهٔ نقشه‌کشی» (first option: fully accepted) | Record the result |
| 5 | `demo.planning` | قراردادها: «قرارداد لازم نیست» | Decide that no contract is needed (a contract request would add the contract steps) |
| 6 | `demo.planning` | تعریف آیتم‌ها و مرحله‌ها: «ثبت آیتم», «ثبت مرحله», «ارسال همهٔ آیتم‌ها به صف کار واحدهای تولیدی» | Define item `COR.10` (quantity 10, a material), one stage on a production unit (e.g. فرزکاری), release it |
| 7 | `demo.supervisor` | نوبت‌های کاری: «تخصیص و شروع» | Dispatch the stage to a machine and operator (an operator not scheduled for the shift needs the off-shift reason), then «پایان نوبت» |
| 8 | `demo.planning` | نوبت‌های کاری: «تأیید زمان واقعی نوبت پایان‌یافته» | Approve the session time (never the person who dispatched it) |
| 9 | `demo.production` | نوبت‌های کاری: «ارسال مرحله به کنترل کیفیت» | Submit the stage to QC |
| 10 | `demo.qc` | بازرسی کنترل کیفیت | Enter 10 in the accepted box, «ثبت بازرسی» (**the completed QC approval**) |
| 11 | `demo.warehouse` | دریافت انبار و تکمیل: «ثبت دریافت نهایی» | Record the receipt |
| 12 | `demo.warehouse` | هزینه و قیمت: «محاسبهٔ هزینه», «ارسال قیمت به مدیریت» | Calculate the cost and send the price to Management |
| 13 | `demo.management` | هزینه و قیمت: «تأیید قیمت» | Approve the price |
| 14 | `demo.warehouse` | دریافت انبار و تکمیل: «تکمیل/ارسال شد», then «ثبت ارسال» | Mark finished/sent and record the **customer dispatch** (waybill number) |

Part 2 - the correction

| # | Account | Page / button | What to show / say |
|---|---|---|---|
| 15 | `demo.qc` | بازرسی کنترل کیفیت | The approved stage is listed as «تأییدشده (قابل اصلاح توسط کنترل کیفیت)». Enter 3 in the rework box and a reason, press «اصلاح تأیید قبلی». Only QC has this button |
| 16 | `demo.management` | درخواست‌ها -> `COR-1` | The red box **«هشدار برای مدیریت»**: the correction came after customer dispatch; the dispatch is kept as a historical record and was **not** reversed; production, receipt, cost, price and approvals may no longer be current; a new approval may be needed. The item row reads «اصلاح‌شده؛ سوابق وابسته غیرجاری است و اقدام لازم دارد» |
| 17 | `demo.warehouse` | دریافت انبار و تکمیل | The item shows its earlier receipt and finished mark **still present but not current** and the dispatch flagged; «تکمیل/ارسال شد» is refused. Costing (هزینه و قیمت) shows the earlier snapshot/approval no longer current |
| 18 | `demo.production`, `demo.qc` | نوبت‌های کاری -> «ارسال مرحله به کنترل کیفیت»; بازرسی -> enter 3 accepted | The reworked units go through QC again (the stage is executable work again) |
| 19 | `demo.warehouse`, `demo.management` | هزینه و قیمت: «محاسبهٔ هزینه», «ارسال قیمت به مدیریت»; «تأیید قیمت» | **Recalculation and the new Management approval** (a second snapshot; the first stays as history) |
| 20 | `demo.warehouse` | دریافت انبار و تکمیل: «ثبت دریافت نهایی», «تکمیل/ارسال شد» | A **real new receipt** (sequence 2) and the finished mark; the original dispatch is still listed, flagged, never undone. The item row now reads «اصلاح‌شده؛ ...» without the red "needs action" state |

Which statements are verified: the service-level behaviour of every step 10-20 by `EndToEndDemoTests` and `QcCorrectionTests`; the indicator, warning text and button gating by the Desktop view-model tests; the button's presence for QC and absence/disabled state for the other roles by the Windows UI-interaction script. The steps 1-9 in the UI follow `docs/DEMO_WALKTHROUGH.md`.

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
