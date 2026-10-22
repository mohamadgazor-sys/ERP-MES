# Raw-Material Availability Inquiry - Design (R1)

**Status: DESIGN ONLY. Not implemented. Everything marked PROPOSED is not approved** (see `WORKFLOW_REDESIGN.md`, legend and register U-06..U-11).

## 1. Purpose and boundaries

[REQ] After (or while) Drawing determines technical feasibility, Drawing can ask Warehouse whether specific raw materials are available. Warehouse answers each requested line Yes or No, with optional notes.

- [REQ] Material availability does **not** approve technical feasibility and does **not** authorize production.
- [EXISTING] Decision Log #17: the inquiry is informational; no inventory is reserved or deducted. This design keeps that.
- [EXISTING] Master 6.4 / 17.1 and Domain 13.1 describe an earlier inquiry with AVAILABLE / PARTIAL / UNAVAILABLE and an approved quantity. This design replaces that response vocabulary only if the owner approves U-09; until then the earlier text stays in force.

## 2. Entities [PROPOSED]

| Entity | Key fields | Notes |
|---|---|---|
| `RawMaterialInquiry` | Id, RequestId, RoundNo, Status, CreatedBy (Drawing user), CreatedAt (UTC), SentAt, ClosedAt, ClosedBy, CloseReason, Note, RowVersion | One request may have several inquiries or rounds. Anchor at request level (U-07). |
| `RawMaterialInquiryLine` | Id, InquiryId, LineNo, RequestItemId (optional), MaterialGrade (e.g. ST37), MaterialForm (bar, tube, plate...), DiameterMm, LengthMm, OtherDimension (optional), RequiredQuantity, QuantityUnit, StandardOrSpec (optional), Condition/Remarks (optional), RowVersion | Attributes beyond grade, diameter and length are included only where genuinely needed to identify stock (U-08). Dimensions in millimetres, decimal. |
| `RawMaterialInquiryResponse` | Id, LineId, Answer (YES / NO), Note (optional), RespondedBy (Warehouse user), RespondedAt (UTC), SupersededByResponseId | Append-only. A changed answer is a new row; the earlier row is kept. |
| `RawMaterialInquiryMessage` | Id, InquiryId, LineId (optional), Direction (DRAWING_TO_WAREHOUSE / WAREHOUSE_TO_DRAWING), Text, CreatedBy, CreatedAt | Clarification history; append-only. |
| `RawMaterialInquiryEvent` | Id, InquiryId, EventType, FromStatus, ToStatus, ActorUserId, At, Reason | Status history; may reuse the audit log. |

Material master (U-08): a Warehouse-owned list of materials (grade, form) is recommended so that grade strings are consistent; free text allowed but flagged. Not approved.

Constraints [PROPOSED]: a line needs grade, diameter and length (or an explicit "not applicable" marker for forms where a dimension is meaningless); quantities positive; one current response per line; a response only on a sent inquiry; users recorded from the authorized session, never typed.

## 3. Quantity: Yes/No versus partial

[REQ] says explicit Yes or No per line. Real stock can cover only part of the requirement. [PROPOSED, U-09]: keep the answer binary, record the required quantity on the line, and let Warehouse state "available quantity" in the note when the answer is No because of shortfall. If the owner wants a machine-readable partial quantity, add an optional `AvailableQuantity` field to the response; this is a decision, not assumed.

## 4. Status model [PROPOSED]

Inquiry statuses (separate from the request status; U-05 recommends no new request status):

```text
DRAFT --send(Drawing)--> SENT
SENT --warehouse answers some lines--> PARTIALLY_ANSWERED
SENT|PARTIALLY_ANSWERED --all lines answered--> ANSWERED
SENT|PARTIALLY_ANSWERED --clarification asked(Warehouse)--> CLARIFICATION_REQUESTED
CLARIFICATION_REQUESTED --Drawing replies / edits lines--> SENT   (round stays; lines edited => revision recorded)
ANSWERED --Drawing reviews--> CLOSED
DRAFT|SENT|PARTIALLY_ANSWERED|CLARIFICATION_REQUESTED|ANSWERED --cancel(Drawing, reason)--> CANCELLED
```

- Lines are editable by Drawing only in DRAFT or after a Warehouse clarification; a send after editing starts a new revision with the old content preserved.
- Warehouse cannot edit lines; Drawing cannot answer lines.
- Every transition is audited with actor, time, and reason where marked.

## 5. Who may do what [PROPOSED where not stated]

| Action | Drawing | Warehouse | Planning | Management |
|---|---|---|---|---|
| Create / edit draft, send, cancel, close | Y | | | |
| Answer lines, ask clarification | | Y | | |
| Reply to clarification | Y | | | |
| Read inquiry and answers | Y | Y | R (PROP) | R (PROP) |

Default deny; Planning and Management read access is U-11.

## 6. What happens when a material is unavailable [PROPOSED - U-10]

Availability is information; it never decides the outcome. Recommended routing:

1. A "No" on any line does not change the request status and does not block the Drawing technical result.
2. All lines with their answers and notes are visible to Drawing; the inquiry reaches ANSWERED.
3. Drawing chooses one of: (a) revise the lines (different grade or dimension, accepted alternative) and open a new round; (b) record the unavailability in the technical result as a condition or note; (c) return the result unchanged.
4. The technical result carries a read-only summary: not inquired / pending / all yes / some no. Planning sees it when deciding the contract and the supply route (the existing Planning supply decision, Master 6, remains the place where purchase or external supply is decided).
5. Production release (WORKFLOW_REDESIGN section 5) requires that inquiry lines for the item are answered and any "No" has been dispositioned (U-10). Not approved.

Information returned to Drawing: per-line answer, notes, responder and time, the clarification thread, and which round the answer belongs to.

Open questions for the owner (U-06, U-10): may Drawing return a technical result while an inquiry is still open; who is notified when Warehouse answers; whether an unavailable-material outcome should force a mandatory Drawing decision before the result is returned.

## 7. Audit and history

Append-only responses and messages; status events; user ids and UTC timestamps stored, Jalali shown in the UI; no deletion of answered inquiries. Superseded revisions are retained.

## 8. Not decided here

Notification mechanics (dashboard badges are the approved pattern, Decision Log #18); inventory reservation (excluded by #17); linkage to purchasing (out of scope for v1).
