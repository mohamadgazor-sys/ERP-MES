# External supply and external processing stages - design note

| | |
|---|---|
| **Status** | PROPOSAL, nothing implemented. Written 2026-11-05 after the requirement audit of the session log of that date. Rows marked **APPROVED** quote an existing decision; rows marked **OPEN** need the owner's answer before code; rows marked **DEFAULT** are what I would build if the owner says "use your defaults". |
| **Sources** | Master Specification §1.5, §10.2, §10.4, §16, §17.3, §13.3; CLAUDE.md "External supply is represented as a production stage..."; Decision Log #5, #15, #95 D7, #101, #127, #128; Domain Specification (stage, production unit). |

## 1. What is already decided (APPROVED)

- **#5 / CLAUDE.md:** external supply is a production *stage* with dispatch/arrival tracking and QC; quantities may be accepted, reworked or scrapped (scrap is permanent). If stage 1 is external, the item waits for arrival/return and QC approval before stage 2 is released. QC approval of the preceding stage gates every later stage, external or not.
- **#15:** an external stage needs an **expected lead time**.
- **§16.2:** a production unit (route) can be classified as external; the Access categories (plating, heat treatment, casting, ceramic, grinding, gear cutting, Tehran, other) may be normalised but the internal/external distinction must stay. A stage is external when its assigned unit is external.
- **§10.2 / §16.4:** releasing an item whose first stage is external registers the dispatch; the stage becomes `EXTERNAL_SENT` and never appears in an internal unit queue.
- **§10.4 / §16.5:** after QC approval of the preceding stage a later external stage becomes `WAITING_EXTERNAL_SEND`; the send is then registered (`EXTERNAL_SENT`).
- **§16.6 / §17.3:** the **Warehouse** records the return; the time is the transaction time (never typed); the stage becomes `WAITING_QC` and goes through the normal stage QC. A return is **not** the final warehouse receipt (#95 D7, #105).
- **§16.7:** one operational "external work" view (request, part, stage, external unit, status, send and return information, notes), with the underlying concepts kept separate.

## 2. Proposed model

| Element | Proposal |
|---|---|
| Production unit | New `Kind` (Internal / External) and, for external units, an `ExternalCategory` from the §16.2 list (normalised codes). Program Admin maintains it with the unit (#122). An external unit has no shifts, machines or operators; the calendar and work sessions ignore it. Changing Internal/External is refused while any non-closed stage uses the unit. |
| Stage | `ExpectedLeadTimeDays` (required for an external stage, #15; refused for an internal one). Two new statuses: `WaitingExternalSend` and `ExternalSent`. `PlannedMinutesPerPiece` is not used for an external stage (DEFAULT: Planning enters 0). |
| Dispatch | `ExternalDispatch` (append-only): stage, sent at / by, sent base units, piece description, note; returned at / by, returned base units, return note; status `SENT` / `RETURNED_TO_QC`. A return moves the returned units to "awaiting QC" exactly like a production submission (#101), so QC records accepted / rework / scrap per unit as today. |
| Rework | Rework units of an external stage go back to `WaitingExternalSend` (they must be sent again), not to an internal queue. |
| Forecast | An external stage does not consume unit time; its planned return = send time (or, before sending, the earliest possible send) + lead time. Downstream internal stages keep waiting for its QC (#5). |
| Screens | "کار بیرونی" (external work) inbox per §16.7; release / QC screens show the external status; Planning enters the lead time where it defines the stage and in the Excel import (new column). |
| Permissions | DEFAULT: Warehouse registers send and return (§16.5 "warehouse/external workflow", §16.6); Planning, Management, Production Manager, QC read. |

## 3. Questions for the owner (OPEN)

1. **Who registers the SEND?** §16.6 names the Warehouse for the return; §16.5 says "warehouse/external workflow" for the send. DEFAULT: the Warehouse records both. Alternative: Planning records the send (it chooses the contractor).
2. **Partial sends and returns.** May one external stage be sent in several shipments (e.g. 60 of 100 pieces now, 40 later) and come back in several returns? DEFAULT: yes, several dispatch rows per stage, each with its own quantity; QC inspects each returned quantity; the stage is complete when every unit is decided (as for internal stages, #101).
3. **Lead time unit and calendar.** Is the expected lead time in **calendar days** (the contractor's time, ignoring our Friday and holidays) or in **our working days**? DEFAULT: calendar days, entered per stage by Planning, optionally defaulted from the external unit. (The Decision Log lists "how external lead time is represented in calendar dates" as unresolved.)
4. **First external stage at release.** §16.4 says release "automatically prepares/registers the external dispatch". DEFAULT: release puts stage 1 in `WaitingExternalSend` and the Warehouse confirms the physical send (with quantity and time), because an automatic `EXTERNAL_SENT` would record a send that has not happened. Confirm, or say that release itself means "sent".
5. **External categories.** Keep the nine Access categories as the fixed list, or let Program Admin maintain the list? DEFAULT: fixed list of the nine (normalised codes), "other" with a note.

## 4. Implementation plan once answered (small sequential pull requests)

1. Unit kind/category + stage lead time + the two statuses (domain, migrations for SQLite and SQL Server, release and QC-gate rules, tests).
2. Dispatch / return records and the Warehouse commands, QC hand-off of returned quantities, rework back to send.
3. External-work inbox screen, lead time in item definition and Excel import, UI smoke walk.
4. Forecast: lead time for external stages.

Nothing in this note changes current behaviour.
