# Demonstration stage progression and QC approval

Status: implemented for the demonstration build (Decision Log #79). It uses the already-approved demo-only Domain transitions and the existing status model. It adds no production
transition, no new status and no change to the meaning of an existing one.

## 1. Actions, states and who may do them

| Action | Stage state change | Department (permission) | Also |
|---|---|---|---|
| Release the first stage | PLANNED -> WAITING | Planning (`ReleaseStage`) | Only stage 1; later stages are never released by hand |
| Start | WAITING -> IN_PROGRESS | Production (`RunStage`) | The Domain requires the preceding stage to be QC approved |
| Submit for QC | IN_PROGRESS -> WAITING_QC | Production (`RunStage`) | |
| QC approval, whole quantity | WAITING_QC -> QC_APPROVED | QC (`InspectOrCorrectQc`) | Appends a `QcApproval` (approver, time, whole planned quantity) and makes the next stage WAITING |

The department mapping of the first two permissions is an **implementation assumption** derived from Master Specification 1.4A and 25.4, not an approved matrix row (Decision Log
"Unresolved"). Anything else - QC rejection, rework, scrap, partial quantities, external supply, route editing, scheduling, Excel import - is not here.

All four act on the item's **current stage**: the first stage QC has not approved. The next stage becomes available only when QC approves the preceding one; a request to start,
submit or approve it earlier is refused by the Domain whatever versions the caller names (tested).

## 2. Enforcement

1. `StageWorkService` reloads the user from the database and checks the permission (a deactivated, forged or stale principal is refused; nothing is changed).
2. It names the expected versions of the **request row, the item row and every stage row the action changes or relies on**: the stage itself; the preceding stage (start,
   approval); the following stage (approval, which releases it). They come from the entry the user saw (`StageTarget.From`). A missing version is refused before anything runs.
3. `IRequestRepository.UpdateRowsAsync` compares every named row **before** the Domain method runs. A stale version gives a concurrency conflict and nothing is saved. A row that
   is not found is "not found".
4. The Domain method validates the transition and the QC gate; an invalid transition (including a repeated action on fresh data) is refused as "invalid state".
5. The stage change, the QC approval and the **audit entry** are saved in **one transaction** (`UpdateRowsAsync` with an audit factory). If anything fails, none of them is saved.

### The actual guarantee

Per row, not per aggregate (Decision Log #77). The named rows are protected against a stale view, and every row the action changes is protected until it is saved. A row of the same
request that the action neither names nor changes (for example another item of the request, or a stage two places away) is not compared. A repeated click on a screen that has
not refreshed is a conflict, so an action never runs twice.

## 3. Audit

One append-only `AuditLog` entry per successful action: `StageReleased`, `StageStarted`, `StageSubmittedForQc`, `StageQcApproved`; user id; machine and OS user; UTC time;
target `ItemStage` with `itemId/stageNo`; and a summary such as `Request R-1, item 1.10, stage 1 (Stage 1) approved by QC (whole quantity 10): WAITING_QC -> QC_APPROVED; stage 2:
WAITING_PREVIOUS_QC -> WAITING`. Refused, conflicting and failed actions write nothing. No password, token or other secret is written. The QC approval is also in the
append-only `QcApprovals` history. There is no screen that lists the audit entries yet.

## 4. Screen

The page «مراحل و کنترل کیفیت» (Persian, right to left, Persian digits) lists the items in the overview's provisional display order with the current stage, its status, the QC
position and the labelled estimate. Four buttons mirror what the Application reports for the selected row and the user (permission and stage state) and are disabled otherwise;
the Application enforces the same rules again. Every action ends with a Persian message (success in green, refusal or failure in red) and a reload of the list, so the screen
shows what is true now. It is a snapshot, not live, and not the authoritative per-unit operational queue. The read-only overview shows the new state when it is opened or reloaded.

## 5. Demonstration accounts

`demo.production` (Production, Staff) was added to the fictional accounts (now eight) for the walkthrough: `demo.planning` releases, `demo.production` starts and submits,
`demo.qc` approves, `demo.management` watches.
