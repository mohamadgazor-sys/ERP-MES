# Requirements review for the first milestone (2026-11-15)

**Scope and honesty.** This is a targeted review of every canonical business rule in `CLAUDE.md`, the owner decisions recorded up to Decision Log #146, and the explicit "must" statements of Master Specification sections 12, 17 and 18, against the code and the tests. It is **not** a line-by-line audit of the whole 3,000-line specification; sections not named here were not re-read in this review. Nothing here reopens a decided rule. "Evidence" names the place to check.

## Result in one sentence
Two approved items had been deferred and were genuinely missing (contract search, approved-contract dashboard notice); both are implemented in the same close-out (Decision Log #147). No other approved requirement was found unbuilt. The rest of what is open is an owner or IT action, not missing functionality (last section).

## Canonical rules (CLAUDE.md) against the implementation

| Rule | State | Evidence |
|---|---|---|
| WPF, MVVM, Persian, right-to-left | Done | Desktop project; UI smoke opens every page |
| Windows 10/11 only, .NET 10 | Done | `global.json`, project files |
| SQLite for development, SQL Server for production after approval; logic independent of the provider | Done; production instance is IT's | providers, migrations, SQL Server CI pass (#117) |
| Request is the central container; projects not mandatory | Done | domain model |
| No internal request workflow, no emergency subsystem | Done (absent by design) | none present |
| Request type MAKE or REPAIR | Done as decided in #84 (one authoritative type per request; supersedes the item-level wording of #2) | Decision Log #2, #84 |
| Customer RequestNo free text; unique by customer, number and Jalali year; case-sensitive; never silently changed | Done | request tests |
| Contract: Planning creates, Management approves with optional text, rejection returns, customer derived, **dashboards informed**, item-scoped decision (#111), amount (#146) | Done; the dashboard notice and search were the two gaps (#147) | contract tests, `DashboardTiles.ContractNotice` |
| QC approval gate before every later stage; Planning releases the first stage; external first stage waits for return and QC | Done | stage tests, external work tests |
| External supply is a stage with dispatch, arrival, QC, lead time; permanent rejection is scrap; rework returns; contractor-lost pieces | Done (#129, #136) | external work screen and tests |
| Later QC correction: history kept, item marked corrected, downstream not auto-reset | Done (#143) | QC correction tests |
| Jalali dates with 24-hour time; Rial only | Done | date service, no Toman conversion |
| QC owns UOM definitions, reciprocal conversions derived | Done | `UnitConversion`, QC master screen |
| Planning machine rate per hour and normal priority; exact labels; shared priorities | Done | priority labels in `Priority.cs`, rate screens |
| Minutes per piece x planned quantity, calendar, queue, capacity, downtime, external lead time forecast | Done as decided (one lane per unit, B13 keep) | scheduling tests |
| Management-only multi-row queue override with old/new order, reason, who and when; Planning's priority preserved | Done | queue override history |
| Saturday-Thursday week, Friday by exception with reason, configurable calendar and shifts, not hard-coded | Done | calendar and shift screens |
| Maintenance reports downtime; it affects the forecast and is auditable | Done (#128, #130) | downtime screen and tests |
| Excel import: one sheet, Persian headers, work-type column, about 1,000 rows, preview, one transaction, manual conflict choice | Done | import service and tests |
| Management live production overview following Planning and override priorities | Done | production queue screen |
| Permissions: QC inspects, Drawing decides doability, Planning contracts, Management approves requests and contracts, Warehouse receives, Planning+Management priorities and rates, Warehouse+Management material price, Admin users | Done | `AuthorizationRulesTests` matrix |
| One stored PDF may be linked from several records, bytes stored once | Done | document links, content hash |
| Reward and ranking formula | Phase 2 as decided; reporting infrastructure only | KPI screen |
| Decisions of 2026-11-14 (A1-E5, Q1-Q8, R1) | Done or "keep"; see `OWNER_DECISION_STATUS_2026-11-14.md` | Decision Log #142-#146 |

## What remains open and why it is not missing functionality
- **Production SQL Server instance, edition, backups, connection strings:** IT (Decision Log #142, `SQL_SERVER_PRODUCTION_ARCHITECTURE.md`).
- **Real overtime limits (A1):** Management enters them before go-live; the demo values are examples.
- **Physical acceptance of the final package and of the new live demo steps:** a person (the owner), not yet done.
- **Seeded demo data for the new scenarios** (an already-dispatched item, a report amendment, a reversal): not seeded; documented as live steps. A choice, not a requirement.
- **Persistent "read/unread" contract notifications:** not required by the specification text; the derived tile satisfies 18.5. Only needed if the owner wants acknowledgement.
- **Code signing, installer, production backup of the application database, antivirus allow-listing:** deployment tasks (see the report to the owner).
