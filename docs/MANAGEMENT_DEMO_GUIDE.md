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

## 2c. QC correction after customer dispatch (owner decisions Q1-Q8, Decision Log #142/#143; needs the package built from the main that contains it)

The seeded demonstration data has **no** item that has already been received, priced and dispatched, so this scenario is shown on an item that is taken through the steps live, or described from the automated end-to-end test (`A_QC_correction_after_receipt_price_and_dispatch_...` in `EndToEndDemoTests`). Seeding a ready-made dispatched item is a separate follow-up so that the offline package and its physical-test record are not changed by this feature.

| Step | Account | Page | What to show / say |
|---|---|---|---|
| 1 | `demo.qc` | بازرسی کنترل کیفیت | A QC-**approved** stage is listed as «تأییدشده (قابل اصلاح توسط کنترل کیفیت)». Type the rework/scrap count and a reason, press «اصلاح تأیید قبلی». QC alone has the button; every other role has it disabled or does not see the page |
| 2 | any | درخواست‌ها -> request | The item grid column «تأیید کنترل کیفیت» now shows «اصلاح‌شده توسط کنترل کیفیت» (orange) or, when the cost/price/receipt/finished records are no longer current, «اصلاح‌شده؛ سوابق وابسته غیرجاری است و اقدام لازم دارد» (red); ordinary approved items show «تأیید عادی» |
| 3 | `demo.management` | درخواست‌ها -> request | When the item had **already been dispatched to the customer** a red box «هشدار برای مدیریت» states: the correction came after the customer dispatch; the dispatch is kept as a historical record and was **not** reversed; production, receipt, cost, price and approvals may no longer be current; a new approval may be needed |
| 4 | `demo.warehouse`, `demo.management` | هزینه‌یابی, تحویل و ارسال | Recalculate the cost, Management approves the new price, Warehouse records a real new receipt (sequence 2) and the finished/sent mark. All the first records stay visible as history |

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
