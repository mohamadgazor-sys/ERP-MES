# Domain Model: Customer, Request, Items, Stages, Priority (demonstration scope)

| | |
|---|---|
| **Status** | Implemented in `src/ERP.MES.Domain/Requests/` with tests in `tests/ERP.MES.Domain.Tests/Requests/`, for review; the owner's decisions on G1 to G11 are applied (section 3). Pure domain types: no persistence, no EF Core configuration, no migrations, no UI. |
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
| `Priority` | `VERY_URGENT` (1, highest), `URGENT` (2), `NORMAL` (3) with the exact labels "خیلی فوری", "فوری", "عادی"; several items may share a priority; stored on the item and inherited by its stages; new items default to `NORMAL`; Planning can change it | DS 18; Master Spec; #58 |
| `ItemStage` | Item-specific ordered stages; planned minutes per piece, planned quantity; `PlannedTotalMinutes` = minutes per piece x quantity, without rounding | DS 6.1, 19 |
| QC gate | Stage N + 1 cannot become executable (or be started or approved) until QC has approved stage N; Planning releases the first internal stage; approval opens the next stage; approvals are kept as append-only records. The release/start/submit transitions and the single-quantity QC approval record are demonstration-only | DS 9.2, 2.5; #58 |
| Timestamps | UTC `DateTime`; calendar dates `DateOnly` | #24 |

## 2. Not modelled (outside this change)

Request status transitions and workflow history; contracts; documents; persistence, keys beyond plain `long` ids, version columns, EF Core mapping, migrations; authorization (the application layer checks, for example, that only QC approves); QC rejection, rework, scrap and partial quantities (a production requirement the model does not represent yet); external-supply stages and lead times; the audited controlled route revision (the model has initial route definition only); work sessions; scheduling dates, calendar and capacity; Management queue override; UOMs and conversions; materials, repair details, costing and anything monetary.

## 3. Guards and choices, and the owner's decisions (2026-10-14)

The specification did not state these; the owner reviewed G1 to G11 and decided them (Decision Log #58). "Demo-only" means labelled for the demonstration and not a production rule.

| # | Choice in the code | Decision |
|---|---|---|
| G1 | `RequestNo` rejects empty, white-space-only and leading/trailing white space; never trimmed or normalised | Confirmed |
| G2 | `Customer.Name` non-null, non-empty, not white space; no other formatting, case or uniqueness rule; stored as given | Confirmed |
| G3 | Requested and planned quantities greater than zero; exact decimals kept, no rounding; the scale stays open for Milestone 2 | Confirmed |
| G4 | Planned minutes per piece of an internal stage greater than zero; zero never means "no estimate"; external lead time is a separate concept (not modelled yet) | Confirmed |
| G5 | `ItemNo` exact, case-sensitive ordinal comparison; never parsed numerically | Confirmed |
| G6 | Planning priority is stored on `RequestItem`; stages and queue entries inherit it; no stage-priority field; Management overrides stay separate (not modelled here) | **Decided, recorded in the specification and Decision Log #58** |
| G7 | New items default to `NORMAL`; Planning may change it | Confirmed |
| G8 | Release, start and submit-for-QC transitions | Approved for the labelled demonstration only; does not approve the production stage state machine or the request-status table |
| G9 | **Initial route definition is separate from route revision.** `DefineInitialStage` builds a route and is allowed only before the first stage is released; it is not presented as, and must not be used as, route revision. The approved controlled revision after work has started (authorized Planning actor, reason, timestamp, before/after sequence, audit record; downstream handling still undecided) is **not implemented and not prohibited**. The demo uses fixed seeded routes and has no route editor | Corrected (twice) |
| G10 | `ApproveStageWholeQuantity` approves the whole planned quantity; `QcApproval` holds a single approved quantity | **Demo-only.** The separate accepted, rework and scrap quantities of DS 9.3 are a **future production requirement**: this model does not represent them yet and a richer QC inspection record will be needed |
| G11 | Cancelled and closed requests accept no new items | Corrected. "Ordinary item definition belongs before production release; later changes follow the controlled Planning process" is **not enforced**: no request-level production-release state exists yet, and the status transitions and permissions that govern it are deferred to the workflow and authorization work. `AddItem` must not be read as "items may be added in every other state" |

Remaining open (not decided by this change): downstream-stage handling after a route revision; the production stage state machine; the request-status transition table; precedence of an active Management override after a later Planning priority change; enforcement of the release limit for item edits.

## 4. Testing

Focused unit tests cover: request-number acceptance and rejection, case sensitivity and near duplicates; customer data; Jalali-year derivation at Nowruz and Esfand boundaries; the business key across customers and years; mixed MAKE/REPAIR items; item-number uniqueness; priority labels, codes, ordering, shared priorities and changes; stage numbering and initial statuses; the planned total minutes formula; the QC gate (blocked until approval, unlocked after, no out-of-order approval, no double approval); whole-route completion; initial route definition before release and its refusal afterwards (no unaudited post-release change, no revision-like method); stages inheriting the item's priority; the default priority; cancelled and closed requests rejecting new items; and exact, non-numeric item numbers.
