# Request Status Transitions — DRAFT for approval

**Status update (request lifecycle PR, after Decision Log #82):** the owner's lifecycle (WORKFLOW_REDESIGN section 3, steps 2-5) states rows 2, 3, 4, 5, 7 and 8 explicitly; they are **implemented** (rows 2-5, 7, 8 with a history entry and an audit entry in one transaction). Row 9 (send back to Drawing) is implemented as a **labelled default, not approved**, because the lifecycle dead-ends without a way to answer "more information needed". Rows 6, 10 and 11 (cancellations) and everything after `DRAWING_RETURNED_TO_PLANNING` are **not implemented**. The permission mapping used: approve/reject = Management with the Manager role; reopen = Planning or Management; forward and send back = Planning (the "receives the request" permission); Drawing result = Drawing. A technical note is required for every Drawing outcome except full acceptance (labelled default). The original text below is kept as history.

**Original status:** Draft. **Nothing here is approved, and nothing here may be implemented.** Review of this table and of the questions in §4 is **deferred until before the request-workflow milestone** (Milestone 2); Milestone 1 does not touch requests, statuses or workflows (see `MILESTONE_1_PLAN.md` §1). It was prepared from Master Specification §3, §5.2 and §6.1, Decision Log #29, #30 and #34, and Domain and Database Specification §4.3, §15.2 and §15.4. The "Basis" column says where each row comes from. Rows marked **Proposed** are my suggestions and need your confirmation before the Milestone 2 state machine is built.

Scope: Milestone 2 only (creation, Management decision, hand-off to Planning and Drawing). Statuses for contracts, production and completion are added with their milestones, so `CLOSED` has no transition into it yet.

## 1. Statuses (Decision Log #29)

`CREATED`, `SENT_TO_MANAGEMENT`, `MGMT_APPROVED_AT_PLANNING`, `MGMT_REJECTED_AT_PLANNING`, `UNDER_DRAWING_REVIEW`, `DRAWING_RETURNED_TO_PLANNING`, `CANCELLED`, `CLOSED`.

## 2. Transitions

Actors use the department/role names of Decision Log #30. Every transition writes one `RequestWorkflowEntry` (from/to department, action code, result, new status, note, actor, timestamp) in the same transaction as the status change. Any attempt not listed is rejected.

| # | From | To | Action | Actor (department / role) | Required data | Basis |
|---|---|---|---|---|---|---|
| 1 | *(none)* | `CREATED` | Create request | Central | Customer, RequestNo, RequestDate (rules of Decision Log #31); request PDF optional | MS §3.2; #34 |
| 2 | `CREATED` | `SENT_TO_MANAGEMENT` | Submit to Management | Central | Required header fields complete | MS §3.2; #34 |
| 3 | `SENT_TO_MANAGEMENT` | `MGMT_APPROVED_AT_PLANNING` | Approve | Management / Manager | none | MS §3.3; DS §15.2 |
| 4 | `SENT_TO_MANAGEMENT` | `MGMT_REJECTED_AT_PLANNING` | Reject | Management / Manager | **Reason (mandatory)** | MS §3.3; DS §15.2 |
| 5 | `MGMT_REJECTED_AT_PLANNING` | `SENT_TO_MANAGEMENT` | Reopen for new Management decision | Planning or Management | **Reason (mandatory)**; audited | #29; DS §15.2 |
| 6 | `MGMT_REJECTED_AT_PLANNING` | `CANCELLED` | Cancel rejected request | Planning | Note | MS §3.3 ("Planning handles the cancellation"); **Proposed** that it ends in `CANCELLED` |
| 7 | `MGMT_APPROVED_AT_PLANNING` | `UNDER_DRAWING_REVIEW` | Send to Drawing | Planning | none | MS §5.1, §6.1; #34 ("records the hand-off to Drawing") |
| 8 | `UNDER_DRAWING_REVIEW` | `DRAWING_RETURNED_TO_PLANNING` | Record Drawing decision | Drawing | Decision outcome and technical note (see §3) | MS §5.2; DS §15.2 |
| 9 | `DRAWING_RETURNED_TO_PLANNING` | `UNDER_DRAWING_REVIEW` | Send back to Drawing (more information / correction) | Planning | Note | **Proposed** |
| 10 | `DRAWING_RETURNED_TO_PLANNING` | `CANCELLED` | Cancel after Drawing decision | Planning | Reason | **Proposed** |
| 11 | `CREATED` | `CANCELLED` | Cancel before submission | Central | Reason | **Proposed** |

Not defined here: any transition into `CLOSED`; anything after `DRAWING_RETURNED_TO_PLANNING` other than rows 9 and 10 (item definition completion, contract determination and production release belong to later milestones).

## 3. Drawing decision outcomes

Master Specification §5.2 lists the outcomes as: fully accepted, partially accepted, rejected, more information needed, ordinary return/correction. They are stored as data on the workflow entry, not as separate request statuses, so row 8 leads to one status for every outcome. This keeps the status set at the approved eight. **Proposed**; confirm.

## 4. Questions for you

1. Are `CANCELLED` and `CLOSED` terminal (no further transitions)? Not stated in Decision Log #29. Proposed: yes.
2. Rows 6, 9, 10 and 11 are my proposals. Which should stay, change or go?
3. Does reopening (row 5) need a role (for example only the Manager role in Management), or is department enough (as in Decision Log #29)?
4. In which statuses may items be created or edited? Proposed: Planning in `MGMT_APPROVED_AT_PLANNING` and `DRAWING_RETURNED_TO_PLANNING`; Drawing (MAKE items) in `UNDER_DRAWING_REVIEW`; nobody once production is released (Decision Log #34). Excel import follows the same rule.
5. May a request be cancelled from `SENT_TO_MANAGEMENT` or `MGMT_APPROVED_AT_PLANNING`? Not in the table; add if wanted.

## 5. Tests this table would produce (Milestone 2)

- Each allowed row succeeds for the named actor and writes exactly one workflow entry atomically with the status change (fault-injection rollback test).
- Every pair not listed is rejected, for every actor.
- Rows 4, 5 and 10 are rejected without a reason.
- Row 3 and 4 are rejected for a Management user without the Manager role, and for every other department.
- Reopening twice in a row from the same rejected state is impossible (the second attempt finds the request already in `SENT_TO_MANAGEMENT`).
