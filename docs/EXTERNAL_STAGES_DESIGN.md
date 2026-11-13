# External supply and external processing stages - design

| | |
|---|---|
| **Status** | **APPROVED** (owner, 2026-11-05; Decision Log #129). **Implemented** in four sequential steps (Decision Log #132-#135; section 6); a step counts as delivered only when its pull request is merged on main. The 2026-11-05 proposal of this file is superseded by this version; its five questions are answered in section 2. |
| **Sources** | Master Specification §1.5, §10.2, §10.4, §16, §17.3, §13.3; CLAUDE.md "External supply is represented as a production stage..."; Decision Log #5, #15, #95 D3/D7, #101, #105, #122, #129. |

## 1. Already decided before this design (unchanged)

- **#5 / CLAUDE.md:** external supply is a production *stage* with dispatch/arrival tracking and QC; quantities may be accepted, reworked or scrapped (scrap is permanent). QC approval of the preceding stage gates every later stage, external or not.
- **#15:** an external stage needs an expected lead time.
- **§16.2:** a production unit (route) can be classified as external; a stage is external when its assigned unit is external.
- **§10.2 / §10.4 / §16.4-16.6:** an external stage never appears in an internal unit queue; it is sent, returned, and the returned quantity goes through the normal stage QC (`WAITING_QC`). The return time is the transaction time (never typed).
- **§17.3, #95 D7, #105:** a return from an external route is NOT the final warehouse receipt.
- **§16.7:** one operational "external work" view with the underlying concepts kept separate.

## 2. Owner decisions of 2026-11-05 (Decision Log #129)

| # | Question | Decision |
|---|---|---|
| Q1 | Who records the send? | The **Warehouse** records both the dispatch and the return. |
| Q2 | Partial shipments? | **Several partial dispatches and partial returns** per external stage are allowed. |
| Q3 | Lead-time unit | **Calendar days** (not working days). |
| Q4 | First stage external at release | Releasing the item does **not** mean "sent": the stage waits (`WAITING_EXTERNAL_SEND`) until the Warehouse explicitly confirms a dispatch. This refines the "release automatically registers the dispatch" wording of §10.2/§16.4. |
| Q5 | Categories | The **exact category list of the Access system** (§16.2) as a controlled list; no new categories, no renaming of the historical codes. |

Additional owner requirements for partial shipments: each dispatch and each return is a separate historical transaction with its quantity, date/time and responsible user; dispatched, returned and remaining quantities are tracked; a return never exceeds what was dispatched; corrections and cancellations keep their audit history; the current external status and the outstanding quantity are visible to the relevant users; an external stage is not completed until the required quantity has come back and the normal QC/completion rules are satisfied.

**Reconciliation note (category count).** §16.2 lists nine codes in total: `INTERNAL_PRODUCTION` and eight external ones (`EXTERNAL_PLATING`, `EXTERNAL_HEAT_TREATMENT`, `EXTERNAL_CASTING`, `EXTERNAL_CERAMIC`, `EXTERNAL_GRINDING`, `EXTERNAL_GEAR_CUTTING`, `EXTERNAL_TEHRAN`, `EXTERNAL_OTHER`). The earlier version of this file wrongly called all nine "external" and listed only eight. The controlled list is these **nine codes exactly**, stored as written; a unit is external when its category is not `INTERNAL_PRODUCTION`. The Persian texts on screen are display labels only; the codes are not renamed.

## 3. Reuse - what is NOT duplicated

| Existing functionality | Reused how |
|---|---|
| Production units (Program Admin, #122) | Gain one field, the category. No separate "contractor" master. |
| Stage definition by Planning (route, unit, catalog title #124) | The same command; an external stage additionally needs the lead time. |
| Stage release and the QC gate (`RequestItem`) | Same transitions; only the status a released/unlocked stage enters differs when it is external. |
| Quantity QC (#101: awaiting / accepted / rework / scrap, shortfall decision) | Unchanged. A return puts the returned base units into "awaiting QC" exactly as a production submission does; QC inspects them with the existing inspection screen. |
| Audit log, row versions, one transaction per change | Unchanged. |
| Warehouse receipt, finished/sent and customer dispatch (#105) | **Not** reused: they are final receipt and delivery to the customer, a different concept (§17.3). External shipments get their own records. |
| Inquiry supply route (#94, "supply type" of §16.1) | Unchanged: it says where material comes from. An external *stage* says who performs an operation. |

## 4. Model

**Production unit.** `Category` (one of the nine codes, default `INTERNAL_PRODUCTION` for existing units). Program Admin sets it with the unit (`MaintainProductionResources`). An external unit has no shifts, machines, operators or queue; the category cannot change while a stage that is not QC approved uses the unit.

**Stage.** `IsExternal` (a snapshot taken when Planning defines the stage, from the unit's category) and `ExpectedLeadTimeDays` (whole calendar days, 1-365, required for an external stage and refused for an internal one). Two new statuses with the §16 codes: `WAITING_EXTERNAL_SEND` and `EXTERNAL_SENT`. An external stage needs a whole number of base units (dispatch quantities are counted in base units).

**Quantities of an external stage** (all in base units; input = what the stage received, as for internal stages):
- *dispatched* = sum of active dispatches; *returned* = sum of active returns; *at the contractor (outstanding)* = dispatched - returned;
- *awaiting QC*, *accepted*, *rework*, *scrap* = the existing QC counters;
- *still to send* = input - accepted - scrap - awaiting QC - at the contractor (rework units are part of it: they must be sent again).

**Status of an external stage** is derived after every dispatch, return, cancellation and QC decision: awaiting QC > 0 -> `WAITING_QC` (QC can act on what came back even while other units are still away); else at the contractor > 0 -> `EXTERNAL_SENT`; else still to send > 0 -> `WAITING_EXTERNAL_SEND`; else the existing completion rules (scrap -> shortfall decision; otherwise QC approved and the next stage is unlocked). So the stage is completed only when everything has come back and QC has decided every unit.

**Transactions** (append-only, Warehouse, each with quantity, local date/time = transaction time, user, optional note):
- `ExternalDispatch`: stage, quantity (1 .. still to send), piece description, note.
- `ExternalReturn`: the dispatch it comes back from, quantity (1 .. that dispatch's outstanding quantity, so a return never exceeds what was dispatched), note.
- **Cancellation instead of deletion** (with a mandatory reason, user and time; audited): a dispatch can be cancelled only while nothing has been returned against it; a return only while its units are still awaiting QC (QC has not decided them). A correction is a cancellation followed by a new transaction.

**Visibility.** The external-work view (§16.7) lists, per external stage: request and customer, item number and part, stage, external unit and category, status, dispatched / returned / at the contractor / still to send, expected return date per open dispatch (dispatch date + lead time in calendar days, flagged when overdue), the transaction history with users and notes. Warehouse acts; Planning, Management, the Production Manager and QC read. Request and queue screens show the external status of the stage.

**Forecast.** An external stage uses no unit time; its expected return is the latest open dispatch date + lead time (before any dispatch: today + lead time). Downstream internal stages still wait for QC (#5). (Last PR.)

## 5. Contractor-lost pieces (decided 2026-11-13, Decision Log #136; implemented)

The Warehouse records units a contractor never returns as a `Missing` row against the original dispatch (reason mandatory; at most the dispatch's outstanding quantity). Missing units are not returned, not awaiting QC, not accepted and not scrap. When nothing else is away or awaiting QC the stage is `WAITING_SHORTFALL_DECISION` and Management (or Planning's replacement order) decides it through the existing shortfall decision; the decision records the lost quantity separately from scrap. The next stage receives the accepted base units only; the customer's required quantity is not silently reduced. A mistaken record is cancelled with a reason while undecided. No open points remain in this design.

## 6. Implementation sequence (small sequential PRs)

1. Unit category (nine codes) + stage `IsExternal` / `ExpectedLeadTimeDays` + the two statuses; release and QC-gate transitions; external units excluded from the internal queues; SQLite and SQL Server migrations; tests.
2. Dispatch / return / cancellation transactions with quantity rules, derived status, QC hand-off of returned units, rework back to "still to send"; audit; tests.
3. External-work screen (Warehouse actions, read-only for the others), lead time in item definition and the Excel import, external status on the existing screens; UI smoke walk.
4. Forecast with lead time.
