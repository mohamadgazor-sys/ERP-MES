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
19. **Authentication/roles:** Application username/password; one department and one role per user. Sensitive action permissions are defined in the Master Specification and project instructions.
20. **Platform/data migration:** Windows 10/11 desktop; WPF/MVVM; local SQLite only for single-user development; final production database SQL Server. Confirm supported Windows 10 baselines and exact .NET target before locking framework.
21. **Performance bonuses:** Ranking/reward formula is Phase 2, not a v1 approval/payroll calculation.

## Unresolved — do not guess

- Request progress percentage semantics: proposed item-completion percentage plus separate quantity/status summaries, but not yet explicitly approved.
- Exact minimum Windows 10 release/edition to support; assess current support/security status and test actual factory devices. Prefer Windows 11 where hardware supports it.
- Exact .NET target/runtime and SQL Server version/provider after OS baseline and deployment environment are inventoried.
- Full role × department × operation authorization matrix details beyond the explicitly approved combinations; default deny for sensitive actions until permission is specified.
- Scheduling algorithm details: queue capacity across multiple units/machines, whether per-piece times vary by quantity/routing, setup/batch times, whether parallel machines are interchangeable, how downtime is approved, and how external lead time is represented in calendar dates.
- Priority assignment granularity and ordering rules where multiple items/stages share the same priority; use planning order as a separately stored position.
- Controlled stage-revision semantics after an item has started: insertion before/after existing stages is allowed, but the exact statuses/history/impact on active or completed stages require explicit rules.
- Excel conflict-resolution choices (skip/update/cancel/manual field merge) and whether updating fields is permitted for already released/in-progress/completed items. Never overwrite a production item silently.
- Request progress formula and how partial quantity / scrap / rework impact the displayed percentage.
- PDF allowed formats/max size/storage retention policy and rules for deleting old documents.
- Exact database migration/testing and backup/restore operating procedure.
