# Management Production Queue (read-only)

Status: implemented as a **read-only** screen in the demonstration build. It changes nothing. This page records what it shows, how it is ordered, what it assumes, and the open
questions that must be decided before any write control (priority change, queue-order override, stage progression, QC action) is added.

## 1. What it is - and is not

- An **item-level overview** for Management (Master Specification: "Management needs a live production overview"). Each row is one request item with the request, customer,
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
| اولویت | Planning priority of the item: `عادی`, `فوری`, `خیلی فوری`. Management overrides do not exist yet and are not shown. |
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
| Loading | While the data is read | "در حال بارگذاری صف تولید…"; a second load is ignored until the first ends |
| Rows | Success with at least one item | The grid, the time the snapshot was read |
| Empty | Success, no items | "در حال حاضر آیتمی برای نمایش در صف وجود ندارد." (not an error) |
| Permission denied | The user's **current** permission (reloaded from the database) does not allow it | "شما اجازهٔ مشاهدهٔ صف تولید را ندارید…"; no rows, no data |
| Conflict / vanished record | The repository reports a concurrency conflict or a missing record while reading | A message asking to reload; no rows |
| Database error | Any other persistence failure | The existing "ذخیره یا خواندن اطلاعات ممکن نشد…" message; no rows |
| Unexpected error | A bug | The existing generic message; the window never receives the exception |

Because the screen only reads, a "conflict" can only arise if data changes while it is being read; real expected-version conflicts will matter when write actions exist.

## 5. Authorization - an assumption to confirm

The Master Specification says Management needs a live production overview but no approved permission row says who may open it. This PR adds the permission
`ViewProductionQueue` and grants it to the **Management department only** (any role). It is an **implementation assumption, not an approved rule**: it is read-only, it grants no
write access, and it is checked by the Application against the user as freshly loaded from the database (a deactivated user, or one whose department changed, loses it at the next
load; a forged Management principal is refused). Question for the owner: should Planning (which controls priority and order) or other departments also see the queue?
Widening it is a one-line rule change plus a test.

## 6. Display order - provisional, not the queue policy

Rows are listed by: (1) Planning priority (خیلی فوری, فوری, عادی); (2) request date, older first; (3) request id; (4) item id. Every key is a total order and the last is unique, so the
order is the same on every load and independent of how the database returns rows (tested). It applies **no Management override** (none exists) and the page says that the
rule for equal priorities and for overrides is not approved.

Architecture Specification section 10.2 also describes attention ordering by operational state (IN_PROGRESS, then problem states, then WAITING) and planned end within the unit
queue. That is not applied here: this is not the unit queue, planned end does not exist, and mixing it in would pre-empt the open decisions below.

## 7. Open decisions before any write control (nothing here is approved)

The Decision Log keeps two questions open ("Queue ordering"). Options and a recommendation:

### 7.1 Order among items/stages of equal Planning priority

| Option | Meaning | Consequence |
|---|---|---|
| T1 First come, first served | Older request date, then request, then item (what the page lists now) | Deterministic and needs no new data; Planning cannot express a different order within a priority |
| T2 Explicit Planning position | Planning stores a sequence number per item (the decision log already speaks of "planning order stored as a separate position") | Matches "Planning controls the normal production priority and queue order"; needs a new column/table, a Planning screen, audit; T1 is the fallback for items without a position |
| T3 Earliest planned end / due date | Order by the scheduling engine's planned end | Needs the unbuilt scheduling engine and calendar; cannot be deterministic before it exists |
| T4 Shortest estimated work first | Smaller estimate first | Raises throughput but can starve long jobs and is a policy nobody has asked for |

**Recommendation: T2 with T1 as the deterministic tie-breaker for unpositioned items.** It follows the approved authority (Planning owns normal order), stays deterministic, and
does not depend on the scheduling engine.

### 7.2 Precedence of a Management override after Planning later changes the item's priority

| Option | Meaning | Consequence |
|---|---|---|
| O1 Override stands until Management clears it | The override wins; Planning's new priority is stored and shown beside it | Management's decision is never silently undone; needs a visible "overridden" flag and a way to clear |
| O2 Planning change cancels the override | The newer decision wins; the override log row is closed as superseded | Planning can undo Management by editing a priority, which conflicts with "Management is authorized to override" |
| O3 Override stands but Management is notified to confirm | O1 plus a "needs review" flag/notification on the item | Safest for audit; needs the notification mechanism |
| O4 One-shot override | The override lapses when the stage starts or finishes | Easy to reason about; surprising if Management meant it for the whole item |

**Recommendation: O1, with the "needs review" flag of O3 when Planning changes the priority of an overridden item.** In every case the override is a separate record (actor,
timestamp, old and new order for each affected row, reason, active flag, cleared by/at) that never overwrites the Planning priority, as Decision Log #11 and Architecture 9.2 require.
A sub-question to settle with it: is an override an absolute position or "move ahead of row X"?

Until these are approved, no priority-change or override control is built.

## 8. Not in this PR

Stage transitions, QC approval or rejection, priority changes, Management overrides, per-unit queues, planned end, automatic refresh, export, filters, sorting by column.
