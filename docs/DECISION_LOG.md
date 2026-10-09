# ERP/MES Decision Log

This is a durable record of user-approved decisions. Update it only when the user approves a decision. Keep unresolved items in the separate section below.

## Approved decisions

1. **Request identity:** Customer-provided free-form RequestNo; not generated internally. Unique business key is Customer + RequestNo + Jalali Request Year. RequestNo comparison is case-sensitive. RequestID is the internal primary key.
2. **Request/item model:** Request-based system; no mandatory Project entity. Customer requests may contain both MAKE and REPAIR items. WorkTypeID is at item level.
3. **Excluded scope:** No Internal Request workflow in v1; no Emergency subsystem; no Final-QC concept. QC is required after individual production stages.
4. **Contracts:** Contract requirement applies to the entire request. Planning creates/submits the contract and attaches/imports PDF. Management approves/rejects with optional explanation. Rejected contracts return to Planning for correction/resubmission. Approval notifies Planning and Warehouse dashboards. Contract customer is derived from Request.
5. **Production/QC:** Each subsequent stage waits for QC approval of the immediately preceding stage. Planning normally releases the first internal stage. If first stage is external, wait for arrival/return and QC approval before progressing. External supply is a stage with dispatch/arrival tracking and QC.
6. **QC correction:** Preserve the historical QC decision and mark the item as corrected. Return the item to Stage 1; handle downstream stages manually rather than automatically replaying every downstream stage.
7. **Quantities:** QC permanently rejected quantities are scrap. Accepted quantities continue; only rework quantity returns for rework.
8. **Localization:** Every user-visible date is Shamsi/Jalali with 24-hour time. Every monetary value is Rial, never silently Toman. UI is Persian and RTL.
9. **UOM ownership:** QC defines configurable units/conversions. A positive conversion factor implies its reciprocal automatically.
10. **Cost ownership:** Planning maintains machine machining rate/hour. Warehouse maintains raw-material price/kg.
11. **Priority:** Values are `عادی`, `فوری`, `خیلی فوری`. Planning owns normal priority/order and may change it; multiple stages/items may share a priority. Management alone may batch-override queue order. Every affected row records old/new order, reason, manager, timestamp; original Planning priority remains intact.
12. **Machining time:** Planning enters minutes per piece. Estimated stage load is minutes per piece × planned quantity.
13. **Calendar:** Saturday–Thursday are normal workdays. Friday is normally off but may be worked with supervisor approval. Holidays/off-days and shift patterns are configurable. Current reference schedule: 08:00–17:00 with 12:00–13:00 break; a possible 2-shift setup is 06:00–14:00 and 14:00–22:00; more shifts may be introduced. Do not hard-code current settings permanently.
14. **Downtime:** PM reports machine downtime to Planning; approved/recorded downtime affects planned end calculations.
15. **External lead time:** External production stages require expected lead time.
16. **Excel:** One mixed worksheet, Persian headers, explicit Work Type column, about 1,000 items. Preview/validate before import. Existing same-ItemNo rows with changed imported values are manual-choice conflicts, not silent overwrites.
17. **Warehouse scope:** Inquiry/receipt/movement history only; no real inventory balance/deduction system in v1.
18. **PDF/document links:** Store a document binary once and permit multiple logical record links.
19. **Authentication/roles:** Application username/password; one department and one role per user. Sensitive action permissions are defined in the Domain and Database Specification §15.2 / §32.8 and the project instructions (`CLAUDE.md`).
20. **Platform/data migration:** Windows 10/11 desktop; WPF/MVVM; local SQLite only for single-user development; final production database SQL Server. Confirm supported Windows 10 baselines and exact .NET target before locking framework.
21. **Performance bonuses:** Ranking/reward formula is Phase 2, not a v1 approval/payroll calculation.
22. **Provisional development target (approved 2026-10-09, provisional):** Use the latest .NET long-term-support (LTS) release available when the solution is created, with a `-windows` target framework for the WPF Desktop project only and the plain target framework for Domain, Application and Infrastructure. The exact version must be named and checked against the Windows 10/11 support matrix in the Milestone 1 ADR. This is non-binding for production: the unresolved items below (Windows 10 baseline, final .NET/SQL Server versions) still require validation on real factory machines before framework lock-in.
23. **Keys and concurrency (approved 2026-10-09):** Integer surrogate primary keys (Domain and Database Specification §25.1). Mutable entities carry an application-managed row-version column used for optimistic concurrency, portable between SQLite and SQL Server.
24. **Time storage (approved 2026-10-09):** Timestamps are stored in UTC; calendar-only dates (for example request date) use a date-only type. Conversion to Jalali and local time happens only at the presentation boundary.
25. **Provider conventions (approved 2026-10-09):** Maintain a written provider-neutral subset (no SQLite-only SQL in business logic; avoid decimal aggregates, `DateTimeOffset` and partial indexes in business queries). Keep one EF migration set per provider. The SQLite development database is disposable and is not migrated to production. Run a SQL Server test pass before the stage/QC milestone.
26. **Audit (approved 2026-10-09):** A generic append-only audit table records master-data, configuration and login events, in addition to the domain-specific histories already required (workflow entries, QC, queue overrides, etc.).
27. **Password storage (approved 2026-10-09):** PBKDF2 using the built-in .NET implementation, with a per-user random salt and the iteration count stored with the hash so it can be raised later. Passwords are never stored in plaintext. Parameters are fixed in the Milestone 1 ADR.
28. **Documentation amendments (approved 2026-10-09):** Superseded passages in the specifications are annotated, not rewritten, and mechanical errors (wrong section numbers, stray markers, stale cross-references) are corrected. No business rule text is changed by an amendment. Items that still need an owner decision are left open.

## Unresolved — do not guess

- Request progress percentage semantics: proposed item-completion percentage plus separate quantity/status summaries, but not yet explicitly approved. Also unresolved: how partial quantity / scrap / rework impact the displayed percentage.
- Exact minimum Windows 10 release/edition to support; assess current support/security status and test actual factory devices. Prefer Windows 11 where hardware supports it.
- Exact .NET target/runtime and SQL Server version/provider after OS baseline and deployment environment are inventoried.
- Full role × department × operation authorization matrix details beyond the explicitly approved combinations; default deny for sensitive actions until permission is specified.
- Scheduling algorithm details: queue capacity across multiple units/machines, whether per-piece times vary by quantity/routing, setup/batch times, whether parallel machines are interchangeable, how downtime is approved, and how external lead time is represented in calendar dates.
- Priority assignment granularity and ordering rules where multiple items/stages share the same priority; use planning order as a separately stored position.
- Controlled stage-revision semantics after an item has started: insertion before/after existing stages is allowed, but the exact statuses/history/impact on active or completed stages require explicit rules.
- Excel conflict-resolution choices (skip/update/cancel/manual field merge) and whether updating fields is permitted for already released/in-progress/completed items. Never overwrite a production item silently.
- PDF allowed formats/max size/storage retention policy and rules for deleting old documents.
- Exact database migration/testing and backup/restore operating procedure.
