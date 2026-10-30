# Item lifecycle, request statuses and who may define or import items

Status of this document: a trace of the code on main `79db558` (2026-10-30) plus the rules that Decision Log #121-#126 add. It exists because the owner asked (2026-10-30) to document the actual statuses and permissions before Drawing's import is built. "Today" means the code before the 2026-10-30 changes.

## 1. Request statuses (Decision Log #29; `RequestStatus`)

| Status | Code | How a request gets there | Action and permission that leaves it |
|---|---|---|---|
| Created | `CREATED` | New request (Secretary, formerly Central) | Submit to Management: `CreateAndSubmitRequest` (Central/Secretary) |
| Sent to Management | `SENT_TO_MANAGEMENT` | Submit, or Reopen | Approve or Reject: `ApproveOrRejectInitialRequest` (Management + Manager role) |
| Management approved | `MGMT_APPROVED_AT_PLANNING` | Approve | Forward to Drawing: `ReceiveAndEditRequestDetails` (Planning) |
| Management rejected | `MGMT_REJECTED_AT_PLANNING` | Reject | Reopen (reason): `ReopenRejectedRequest` (Planning or Management) |
| Under Drawing review | `UNDER_DRAWING_REVIEW` | Forward to Drawing, or Send back to Drawing | Record Drawing result: `RecordDrawingDoabilityDecision` (Drawing) |
| Drawing returned to Planning | `DRAWING_RETURNED_TO_PLANNING` | Record Drawing result (outcome FullyAccepted / PartiallyAccepted / Rejected / MoreInformationNeeded / ReturnedForCorrection) | Send back to Drawing: `ReceiveAndEditRequestDetails` (Planning) |
| Cancelled / Closed | `CANCELLED` / `CLOSED` | not modelled as transitions yet | accept no new items and no item changes |

## 2. Where items may be defined or imported TODAY

* **Definition (manual) and Excel import** require ALL of: request not Cancelled/Closed; request status `DRAWING_RETURNED_TO_PLANNING`; last Drawing outcome FullyAccepted or PartiallyAccepted; for a MAKE request a contract status other than "decision missing / in preparation / pending / rejected" (`DefinitionReadiness.Evaluate`, `RequestGateService`). Permissions: `DefineMakeItems` / `DefineRepairItemsAndEditAnyItem` (manual) and `ImportItemsFromExcel` (import), all **Planning only**.
* **Stage definition** (`DefineStageAsync`) and the queue/release of stages evaluate the same gates per item (`RequestGateService.EvaluateItemsAsync`); release additionally needs the inquiry/clarification gates.
* An item can be revised (by import "update") only while `IsDefinitionRevisable`: no stages and no QC approval.
* **Consequence:** while a request is `UNDER_DRAWING_REVIEW` NO item can exist. Drawing works from the request PDF, raw-material inquiries and clarification questions; it cannot create or import items.

## 3. Rules added by Decision Log #125 (Drawing import)

| Actor | Request status | What may be imported | What is rejected |
|---|---|---|---|
| Drawing | `UNDER_DRAWING_REVIEW` only (also after Planning sends the request back) | customer reference, part name, quantity, quantity type, material, weight, length, diameter, work type (must equal the request's), REPAIR fields (delivery date, warehouse code), assembly flag and parent reference | any populated stage column, stage title, priority, production unit, machine, or other Planning routing column: rejected row by row with a clear message |
| Planning | `DRAWING_RETURNED_TO_PLANNING` with all definition gates as today | all columns, including stages, priority, units, machines | unknown stage titles (catalog, #124), unauthorised columns do not exist for Planning |

Unchanged for both: Cancelled/Closed requests refuse; existing items with a route or QC approval can only be skipped; conflicts are never silently overwritten; the matching key is the customer reference (#121); one transaction per commit; audit entries name the actor and their department.

Items created by Drawing exist before the Drawing result is recorded. They gate nothing by themselves: stage definition and release still require `DRAWING_RETURNED_TO_PLANNING`, an accepted outcome and the contract/inquiry/clarification gates. If Drawing's outcome is Rejected, MoreInformationNeeded or ReturnedForCorrection the items simply stay visible and Planning cannot define stages for them until a later accepted outcome (no item deletion exists; none is added).

The existing contract rule of #111 (item-scoped contract decision) now has the item rows available earlier; the request-level definition gate for MAKE requests is not applied to Drawing's creation because Drawing acts before the contract step. The contract gate is still applied before stages are defined and before release.

## 4. Item lifecycle summary after the 2026-10-30 changes

1. Item created (Drawing import in `UNDER_DRAWING_REVIEW`, or Planning import/manual definition in `DRAWING_RETURNED_TO_PLANNING`): number `RequestNo-NNN` assigned by the system; optional customer reference; optional parent (assembly structure editable while no stage is defined in the subtree).
2. Drawing document attached by Drawing (any time before or after; absence only warns).
3. Planning defines stages from the stage-title catalog (gates as today).
4. Stage release: gates as today; additionally, an assembly's first stage waits until every descendant has completed its route with QC approval (#121); an incomplete REPAIR item (no delivery date or warehouse code) cannot be released (#124).
5. Production, QC, warehouse, costing as today.
