# Domain Model: Customer, Request, Items, Stages, Priority (demonstration scope)

| | |
|---|---|
| **Status** | Implemented in `src/ERP.MES.Domain/Requests/` with tests in `tests/ERP.MES.Domain.Tests/Requests/`, for review. Pure domain types: no persistence, no EF Core configuration, no migrations, no UI. |
| **Basis** | Domain and Database Specification sections 2, 4, 5, 6, 7, 9.2, 18, 19; Decision Log #29, #31, #32, #33, #24, #34; Master Specification priority labels. |
| **Principle** | Only decided rules are modelled as rules. Anything the specification does not say is listed in section 3 and left as simple as possible; none of it changes an existing decision. |

## 1. What is modelled, and where each rule comes from

| Type | Rule implemented | Source |
|---|---|---|
| `Customer` | Name, active flag, audit fields only; no code, no contact fields | DS 4.5; #32 |
| `RequestNo` | Free-form text kept exactly as given; leading/trailing white space rejected, never trimmed; no digit or letter normalisation; case-sensitive equality; a case-only difference is reported as a non-blocking near duplicate | DS 4.2; #31 |
| `Request` | Customer + RequestNo + Jalali Request Year is the business key (`RequestKey`, exact comparison); the Jalali year is derived from `RequestDate` through `IJalaliDateService` at creation and then fixed; starts as `CREATED` | DS 4.1, 4.2; #29, #31 |
| `RequestStatus` | The approved eight statuses and their codes. **No transitions**: the table is an unapproved draft | DS 4.3; #29 |
| `RequestItem` | Item number, part name, `WorkType` MAKE or REPAIR per item, requested quantity; one request may mix both; `(Request, ItemNo)` unique | DS 2.3, 5.1, 5.2 |
| `Priority` | `VERY_URGENT` (1, highest), `URGENT` (2), `NORMAL` (3) with the exact labels "خیلی فوری", "فوری", "عادی"; several items may share a priority; Planning can change it | DS 18; Master Spec |
| `ItemStage` | Item-specific ordered stages; planned minutes per piece, planned quantity; `PlannedTotalMinutes` = minutes per piece x quantity, without rounding | DS 6.1, 19 |
| QC gate | Stage N + 1 cannot become executable (or be started or approved) until QC has approved stage N; Planning releases the first internal stage; approval opens the next stage; approvals are kept as append-only records | DS 9.2, 2.5 |
| Timestamps | UTC `DateTime`; calendar dates `DateOnly` | #24 |

## 2. Not modelled (outside this change)

Request status transitions and workflow history; contracts; documents; persistence, keys beyond plain `long` ids, version columns, EF Core mapping, migrations; authorization (the application layer checks, for example, that only QC approves); QC rejection, rework, scrap and partial quantities; external-supply stages and lead times; controlled route revision; work sessions; scheduling dates, calendar and capacity; Management queue override; UOMs and conversions; materials, repair details, costing and anything monetary.

## 3. Guards and choices the specification does not state (for the owner to confirm)

These are the smallest choices needed to write the types. None changes an existing decision; each can be replaced by a decision without touching callers much.

| # | Choice | Why it was needed | Question |
|---|---|---|---|
| G1 | `RequestNo` must not be empty | The business key cannot be empty text | Confirm that an empty customer request number is never valid |
| G2 | `Customer.Name` must not be null or white space; kept exactly as given | "Name" is a minimum field | Any further name rules? |
| G3 | Requested quantity and planned quantity must be greater than zero | A zero or negative quantity has no meaning | Confirm; also the decimal scale is still undecided (#33) so no rounding is applied |
| G4 | Planned minutes per piece must be greater than zero | Same | Confirm (zero minutes for a stage may be legitimate) |
| G5 | `ItemNo` is required and compared exactly (ordinal): `1.1` differs from `1.10`, `A-01` differs from `a-01` | DS 5.2 requires uniqueness but does not say whether item numbers are case-sensitive | Decide case sensitivity of `ItemNo` |
| G6 | Priority is stored on `RequestItem` (the field of DS 5.1), stages carry none | DS 18 says item versus stage is still pending and forbids two independent fields | Decide the granularity |
| G7 | Priority has no default; the caller must give one | No default is specified | Is `NORMAL` the default for new items? |
| G8 | Provisional stage transitions: release (`PLANNED` to `WAITING`), start (`WAITING` to `IN_PROGRESS`), submit for QC (`IN_PROGRESS` to `WAITING_QC`) | DS 7 names the states but defines no transitions; the QC gate needs a minimal path | Confirm or replace when work sessions and QC are specified |
| G9 | Stages can be added only before the first stage is released | Route revision after start is a blocking, unresolved decision (DS 6.2) | Decide the controlled-revision rules |
| G10 | QC approval covers the stage's whole planned quantity | Partial quantity rules (DS 9.3) are not modelled | Part of the QC rejection / partial quantity work |
| G11 | No status check when adding items to a request | Which statuses allow item edits is an open proposal (#34 says only "until production release") | Decide with the status transitions |

## 4. Testing

Focused unit tests cover: request-number acceptance and rejection, case sensitivity and near duplicates; customer data; Jalali-year derivation at Nowruz and Esfand boundaries; the business key across customers and years; mixed MAKE/REPAIR items; item-number uniqueness; priority labels, codes, ordering, shared priorities and changes; stage numbering and initial statuses; the planned total minutes formula; the QC gate (blocked until approval, unlocked after, no out-of-order approval, no double approval); whole-route completion; and the route-extension guard.
