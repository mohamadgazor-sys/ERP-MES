# Owner Decision Packet - Remaining Genuinely Open Decisions

**Status (updated 2026-10-23): ALL EIGHT ITEMS ARE OWNER-APPROVED as recommended (Decision Log #95), with the boundaries recorded there.** The question text below is kept unchanged as the record of what was asked; where it says "open", "recommended default" or "not approved", read it as superseded by #95. Implementation status: D1 and the D8 inquiry-read row are implemented (Phase 2a-ii); the other items are applied in the phase named under each "Blocks" line (D2/D3 phase 5b and 7, D4/D7 phase 7, D5 phases 6a and 6c, D6 phase 8, remaining D8 rows phases 5a, 6b and 8).

**Original status text (history):** all items were OPEN. Each recommendation was a **recommended default, not approved**. Nothing here was implemented. Everything else previously open (U-01..U-40) is answered by your decisions #84-#92, by existing decisions, or covered by a labelled low-risk default listed in `WORKFLOW_REDESIGN.md` section 9. Implementation of phases not listed under "Blocks" is not stopped by this packet.

Reply per item with the letter of the option (or your own rule). Until you answer, the blocked phase builds only the parts that do not depend on the answer.

---

## D1 - Drawing clarification and unavailable raw material

**Decision: APPROVED 2026-10-23 as recommended (options A, A, yes) - Decision Log #95. Implemented in Phase 2a-ii.**

**Choice 1 - who answers a Drawing clarification request, and where does the request sit meanwhile?**

| Option | Consequences |
|---|---|
| **A (recommended)** Drawing records a clarification question; it goes to **Planning**, who answers (and obtains information from Central/customer outside the application if needed). The request stays `UNDER_DRAWING_REVIEW`; the open question is shown on Planning's dashboard and all history is kept. | No new request status; simple; Planning is already the owner of the workflow after Management. |
| B The question goes to **Central**, who answers. | Adds a Central step to a mid-process request; more screens and permissions. |
| C Drawing returns the request to Planning as "more information needed" (existing outcome) and Planning re-forwards it later. | Uses existing statuses only; clarification history is split across two review rounds; less precise. |

**Choice 2 - when Warehouse answers "No" for one or more lines:**

| Option | Consequences |
|---|---|
| **A (recommended)** The inquiry becomes ANSWERED and **all lines with answers and notes return to Drawing**. Drawing decides: revise the lines and open a new round, record the unavailability as a condition in the technical result, or return the result unchanged. Planning sees a read-only material summary and decides the supply route (including external purchase). Production release requires every "No" line to have a recorded Drawing/Planning disposition. Nothing rejects the request automatically. | Keeps availability separate from feasibility (your rule); one extra disposition step before release. |
| B A "No" automatically returns the request to Planning as blocked. | Simple, but contradicts "availability does not approve or block feasibility". |
| C A "No" is information only; no disposition is required. | Fewest steps; production could be released against known missing material. |

**Choice 3 - may Drawing return its result while an inquiry is still open?** Recommended: **yes**, flagged "inquiry open", but production release (not the result) waits for the answer (consequence of C would be release without waiting).

**Blocks:** phase 2a (routing after "No" and the release gate). Entities, sending, answering and history can be built first.

---

## D2 - Is physical-piece identity needed beyond base-unit quantity records?

**Decision: APPROVED 2026-10-23 as recommended (option A) - Decision Log #95. Applied in phase 5b.**

| Option | Consequences |
|---|---|
| **A (recommended)** **No.** QC records "n base units: accepted / rework / scrap" per item and stage, traced to the decision and user. A pair is 2 base pieces; "one of a pair is scrap" is 1 scrap piece. | Simplest correct design; cannot say *which* physical member was scrapped. |
| B Optional **slot** per line (e.g. member 1 of 2 of a pair, or a set member). | Identifies the member only where QC enters it; extra data entry; some reporting complexity. |
| C Unique **serial/unit ids** for every piece. | Full traceability; labelling and data entry for every piece (thousands per request); far more storage and UI; not requested so far. |

**Blocks:** phase 5b (QC quantity-level data model). Choosing A keeps B available later without data loss.

---

## D3 - How rework and scrap affect production totals and cost

**Decision: APPROVED 2026-10-23 as recommended (A, no automatic replacement, no overproduction without a raised ordered quantity, accepted base units) - Decision Log #95. Applied in phases 5b and 7.**

**Choice 1 - cost treatment of scrap and rework time:**

| Option | Consequences |
|---|---|
| **A (recommended)** All approved hours and material consumed for the item (including rework hours and the time and material spent on scrapped units) are **absorbed over the accepted quantity**: per-unit cost rises; scrap cost is also reported separately for visibility. | One consistent formula; matches R4's "per-unit x produced quantity" when "produced" means accepted; hides loss inside price unless the report is read. |
| B Scrap is a **separate loss line**, excluded from the item's price. | Price is not inflated by scrap; Management/Warehouse must track losses elsewhere. |
| C Rework hours charged to the item, scrap loss absorbed by the company. | Mixed treatment; needs a clear rule per case. |

**Choice 2 - shortfall after scrap:** recommended **no automatic action**: Planning either orders a replacement quantity (new production, same item) or Management accepts the short quantity; the item cannot reach `PRODUCTION_COMPLETED` while the shortfall is undecided. Alternatives: auto-replacement (invents production) or accept short by default.

**Choice 3 - over-production:** recommended **not allowed** beyond the ordered quantity unless Planning raises the ordered quantity first; the cost basis is accepted units up to the ordered quantity.

**Choice 4 - quantity basis for "produced quantity":** recommended **accepted base units** (R4 formula); alternative is submitted units (then scrap is priced).

**Blocks:** phase 5b (balances and completion rule) and phase 7 (cost).

---

## D4 - Contract price versus calculated internal cost

**Decision: APPROVED 2026-10-23 as recommended (option A) - Decision Log #95. Applied in phase 7.**

| Option | Consequences |
|---|---|
| **A (recommended)** **REPAIR and MAKE without a contract:** the calculated cost (approved actual time x rate + material) is proposed to Management, who approves or changes the price. **MAKE with an approved contract:** the contract amount is the price; the calculated cost is shown for information (margin visible); no second price calculation, as in Master 19.1. | Matches existing Master 19.1; Management still sees cost vs contract. |
| B The calculated cost always becomes the proposed price, even under a contract. | Contract amount ignored; likely conflicts with contract practice. |
| C Management chooses per item whether contract price or calculated cost applies. | Flexible; an extra decision each time and less predictable reporting. |

**Blocks:** phase 7.

---

## D5 - Overtime approval limits and overlapping shift assignments

**Decision: APPROVED 2026-10-23 as recommended (option A); no numeric limits invented - Planning configures them before overtime approval can be used - Decision Log #95. Applied in phases 6a and 6c.**

**Choice 1 - overtime:**

| Option | Consequences |
|---|---|
| **A (recommended)** Supervisor creates the assignment (operator + machine + start/end); **Planning approves**; configurable limits (defaults to be entered by Planning: maximum overtime hours per operator per day and per week); an assignment outside the limits cannot be approved without Management. Overtime hours use the same machine rate; **no premium** until you supply one. | Matches #13 (supervisor approval for extra hours) plus Planning control of capacity; needs numbers from you for the limits. |
| B Supervisor creates, **Management** approves everything. | Strongest control; more load on Management. |
| C Supervisor creates and it is effective immediately. | Fastest; no check on limits or staffing; conflicts are only flagged. |

You must also supply the **numeric limits** (or say "Planning configures them"), and whether an overtime **premium** exists.

**Choice 2 - overlapping shift templates:** D (08:00-17:00) overlaps A (06:00-14:00) and B (14:00-22:00). Recommended: (i) **a worker is never scheduled in two overlapping occurrences on the same day**; (ii) a **unit** may run overlapping templates only where Planning marks each unit-shift explicitly, each with its own capacity; (iii) a **machine** has at most one operator at a time except an explicit, recorded handover overlap. Alternative: forbid any unit overlap (simpler; D could not coexist with A/B in a unit). Alternative: allow all overlaps (conflicts only flagged; double-counted hours become possible).

**Blocks:** phase 6a (overlap, capacity validation) and phase 6c (overtime).

---

## D6 - Production-unit performance metrics

**Decision: APPROVED 2026-10-23 as recommended, overtime included but shown separately - Decision Log #95. Applied in phase 8.**

R6 fixes operator ranking (valid reported hours, with deterministic tie-break) and forbids a single composite "best unit". The unit metrics need definitions. Recommended default set, each shown separately and labelled actual/planned/accepted/rework/scrap/estimated:

1. Productive hours (approved report time minus downtime).
2. Accepted quantity (base units, QC-accepted, attributed to the unit that ran the stage).
3. QC acceptance rate = accepted / (accepted + rework + scrap) decided in the period.
4. Rework rate and scrap rate.
5. Plan vs actual time (planned minutes per piece x quantity vs approved actual minutes) for completed stages.
6. Utilization = productive hours / scheduled shift hours.

Questions: do overtime hours count in productive hours and utilization (recommended: yes, shown separately); is quantity attributed to the unit that ran the stage even when rework returns to another unit (recommended: yes, to the unit that performed each step); are rankings authoritative before all reports are approved (recommended: no, a report states how many lines are still unapproved).

**Blocks:** phase 8 only; reports can show these labelled figures without being called authoritative until you approve the definitions.

---

## D7 - Warehouse receipt, finished/sent status and customer dispatch

**Decision: APPROVED 2026-10-23 as recommended (option A) - Decision Log #95. Applied in phase 7.**

Your lifecycle ends with Warehouse "marking the item finished/sent" after price approval; earlier specifications (Master 17.2) have a **final warehouse receipt** as the physical completion.

| Option | Consequences |
|---|---|
| **A (recommended)** Three distinct records: (1) **Warehouse receipt** = production output physically received after the last QC (Master 17.2); (2) **Finished/sent** = Warehouse completion mark, allowed only after Management's price approval; (3) **Customer dispatch** (date, document reference, optional) recorded for tracking but not required to close the item in v1. | Matches both documents; three small records; item reaches `FINISHED_SENT` at (2). |
| B One action "finished/sent" that also means received and dispatched. | Simplest; loses the receipt-before-price distinction and any dispatch evidence. |
| C Receipt and finished/sent only (no customer dispatch record). | Fewer fields; dispatch tracking would need a later addition. |

**Blocks:** phase 7 (completion).

---

## D8 - Authorization rows not settled by your requirements

**Decision: APPROVED 2026-10-23 as recommended, with the explicit rules of Decision Log #95. Inquiry read access implemented in Phase 2a-ii; other rows applied with their phases.**

Rows (all denied until you decide); my recommendation in brackets:

1. Planning and Management **read** raw-material inquiries and answers [yes, read-only].
2. Management reads job reports and KPIs for any period [yes, read-only]; a unit supervisor reads KPIs and reports of their own unit [yes, read-only]; operators read their own reports [yes].
3. If a unit has no designated supervisor, Planning reviews and approves its job reports [yes, flagged].
4. May Management or a supervisor start/submit a stage on behalf of an operator, and who corrects a wrong start or submission (demo-only mapping #80 leaves this undecided) [Planning corrects with reason and audit; no one acts for an operator otherwise].
5. Who may cancel an inquiry [Drawing only] and close a clarification [the party that asked].
6. Read access of Central and Production to items and queues beyond their own work [Central reads its requests; Production reads its unit queue].

**Blocks:** phase 6b (job-report review) and the screens that expose reads; other phases use denied-by-default.

---

## Defaults adopted without asking (not approved; changeable)

Low-risk details with deterministic defaults are classified C in `WORKFLOW_REDESIGN.md` section 9 (request statuses after Drawing return, material attribute set, machine-code stability, item-spec fields, operator accounts and supervisor designation, set/pair costing per base piece, time rounding, rate/price date selection, rounding and adjustment authority, job-report field set and workflow, shift storage and midnight attribution, off-day conflict handling, cost trigger and invalidation, Excel conflict rules). They can be implemented now and amended later.
