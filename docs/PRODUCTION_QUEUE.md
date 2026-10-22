# Production Overview / Queue (read-only)

Status: implemented as a **read-only overview** for Management and Planning in the demonstration build. It changes nothing. This page records what it shows, its provisional display
order, the approved decisions (Decision Log #78) that are **not yet implemented**, and the details that are still open.

## 1. What it is - and is not

- An **item-level overview** for Management and Planning (Master Specification: "Management needs a live production overview"). It is a read-only overview with a provisional display order, **not the authoritative operational queue order**. Each row is one request item with the request, customer,
  priority, current stage and QC position.
- It is **not** the per-production-unit queue of released stages of Architecture Specification section 10.1. That queue needs production units, machine assignment, planned end
  and operator data that do not exist yet. Rows therefore include items whose first stage is not released, items without a route, and items whose route is completed; each is
  labelled, nothing is hidden by an invented filter.
- It is **not** live push: it is a snapshot read when the page opens or "بارگذاری دوباره" is pressed, and the page shows when it was read. Changes by others appear on the next
  reload. (Section 10.2 of the Architecture Specification asks for a live queue; automatic refresh is future work.)

## 2. Data and layers

- `ProductionQueueService` (Application) uses the existing `IRequestRepository` only: `ListAsync` for the requests and `FindWithVersionsAsync` for each request's items, stages and
  row versions. No new repository method, no schema change, no migration, no package.
- Loading one request at a time is simple and right for the demonstration data (a handful of requests). It is one query set per request, so a database with thousands of requests
  would need a dedicated read model (a repository query for the queue). That is deferred, not forgotten.
- Each entry carries the item row version and the current stage's row version **as read** (Decision Log #77). The screen does not use them yet; the future write actions must pass
  them to `IRequestRepository.UpdateRowsAsync` as the expected versions of exactly the rows they change.
- The Desktop view model (`ProductionQueueViewModel`) formats text only. It decides nothing about access.

## 3. Columns

| Column | Source |
|---|---|
| ردیف | Position in the display order (Persian digits). |
| شمارهٔ درخواست, مشتری | Request number exactly as stored; customer name. |
| وضعیت درخواست | Request status (Persian text of the existing eight statuses). |
| شمارهٔ آیتم, نام قطعه, نوع کار, تعداد | Item fields; type is ساخت (MAKE) or تعمیر (REPAIR). |
| اولویت | Planning priority of the item: `عادی`, `فوری`, `خیلی فوری`. Planning positions and Management overrides do not exist yet and are not shown. |
| مرحلهٔ جاری | The first stage, in route order, that QC has not approved ("مرحلهٔ ۲ از ۳: title"); "همهٔ مراحل تکمیل شد" when every stage is approved; "مسیر تعریف نشده" when the item has no stages. |
| وضعیت مرحله | Status of that stage: برنامه‌ریزی‌شده (هنوز آزاد نشده), در انتظار تأیید کنترل کیفیت مرحلهٔ قبل, آمادهٔ اجرا, در حال اجرا, در انتظار کنترل کیفیت, تأییدشده توسط کنترل کیفیت. |
| وضعیت کنترل کیفیت | "در انتظار تأیید کنترل کیفیت" when the current stage waits for QC, plus "x از y مرحله تأیید شده"; or "all stages approved". |
| مدت پردازش تخمینی مرحلهٔ جاری (دقیقه) | Planned minutes per piece x planned quantity of the current stage. |
| مجموع مدت پردازش تخمینی مراحل (دقیقه) | The same estimate summed over all of the item's stages. |

"—" means not available (no current stage, or no route). Rejection, rework, scrap and external-supply states are not modelled in the Domain yet, so the queue cannot show them.

### Estimates are not dates

The estimate is **work content in minutes** (Decision Log / demo plan T4). It ignores the working calendar, the queue, machine capacity, downtime and external lead time. The page
says so, and no completion date or "planned end" is shown or calculated anywhere.

## 4. States (all Persian)

| State | When | What the user sees |
|---|---|---|
| Loading | While the data is read | "در حال بارگذاری نمای کلی تولید…"; a second load is ignored until the first ends |
| Rows | Success with at least one item | The grid, the time the snapshot was read |
| Empty | Success, no items | "در حال حاضر آیتمی برای نمایش در صف وجود ندارد." (not an error) |
| Permission denied | The user's **current** permission (reloaded from the database) does not allow it | "شما اجازهٔ مشاهدهٔ نمای کلی تولید را ندارید. این صفحه فقط برای مدیریت و برنامه‌ریزی است."; no rows, no data |
| Conflict / vanished record | The repository reports a concurrency conflict or a missing record while reading | A message asking to reload; no rows |
| Database error | Any other persistence failure | The existing "ذخیره یا خواندن اطلاعات ممکن نشد…" message; no rows |
| Unexpected error | A bug | The existing generic message; the window never receives the exception |

Because the screen only reads, a "conflict" can only arise if data changes while it is being read; real expected-version conflicts will matter when write actions exist.

## 5. Authorization (approved - Decision Log #78)

The permission `ViewProductionQueue` is granted to **active users in the Management and Planning departments** (any role). Every other department is denied by default. It is
read-only and grants no write access. The Application checks it against the user as freshly loaded from the database, so a deactivated user, a forged principal (claiming a
department the database does not give them), a stale principal, and a user who still has to choose a password all get nothing; a stale principal whose persisted department
now allows access is granted what the database says. Views of a single production unit's queue may get separate permissions later.

## 6. Display order - provisional, not the authoritative queue order

Rows are listed by: (1) Planning priority (خیلی فوری, فوری, عادی); (2) request date, older first; (3) request id; (4) item id. Every key is a total order and the last is unique, so the
order is the same on every load and independent of how the database returns rows (tested). **This is not the operational queue order.** It applies no Planning position and no
Management override (neither exists yet), and the page says so.

## 7. Approved target behaviour - NOT implemented in this PR (Decision Log #78)

### 7.1 Order of a production unit's queue

Planning gives each **released stage** in a production unit's queue an explicit **Planning position**. The intended full order is:

1. operational category: **IN_PROGRESS**, then **STOPPED / problem states**, then **WAITING**;
2. within a category: **Planning priority**, then **Planning position** (equal positions: **first come, first served**);
3. then available **planned-end** information;
4. then stable identifiers, as deterministic fallbacks.

It needs per-unit released-stage queues, production units, stopped/problem states, planned-end data and Planning-position storage (a schema change). None of that exists, and this PR
adds no migration and no write control to force it. The overview deliberately does not mimic this order.

### 7.2 Management override versus a later Planning priority change

A Management override **remains active until Management explicitly clears it**. If Planning later changes the priority of an item covered by an active override, the system
preserves **both** the underlying Planning priority and the active override and **flags the override as requiring Management review**; it never silently cancels or replaces the
override. Every override records the actor, timestamp, reason and previous/new order (Architecture Specification 9.2). No override control exists.

### 7.3 Still open (details only)

The definition of "STOPPED / problem" states and of "first come, first served" (which timestamp); the source of planned-end information (scheduling engine); whether a Planning
position is unique per unit; whether an override is an absolute position or "move ahead of row X", and whether it applies to an item or a stage; how the review flag is cleared.

## 8. Snapshot versus live

The page is a **snapshot** read when it opens and when "بارگذاری دوباره" is pressed; it shows the time it was read. It is not live (no push, no automatic refresh). The
specification asks for a live queue (Architecture 10.2); automatic refresh belongs with the future per-unit queue.

## 9. Stage actions live on a separate page (Decision Log #79)

The overview stays read-only. The demonstration stage actions (release, start, submit for QC, whole-quantity QC approval) are on the separate page "مراحل و کنترل کیفیت", which
lists the same items and is also open to Production and QC users (who cannot see the overview). It is not the authoritative per-unit operational queue either. See
`docs/STAGE_WORKFLOW_DEMO.md`.

## 10. Not in this overview

Planning priority edits, Planning positions, Management overrides, QC rejection, per-unit queues and their schema, planned end, SQL Server, automatic refresh, export, filters,
sorting by column.
