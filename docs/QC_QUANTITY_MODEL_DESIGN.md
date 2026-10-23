# QC-Owned Units of Measure, Quantity Types and Individual-Unit Dispositions - Design (R3)

**Status: DESIGN ONLY. Not implemented.** Approved: QC owns units of measure and quantity types as distinct concepts; Planning selects them for items; every quantity type maps through a controlled mapping to the base QC-counting unit; a set's composition is defined explicitly, never assumed (#90); individual-unit QC dispositions for the intended production system (#83, #92). The structures below are PROPOSED/DEFAULT; physical-piece identity and rework/scrap totals are `OWNER_DECISION_PACKET.md` D2, D3.

> **Decision update (2026-10-23):** D2 (no per-piece identity; base-unit quantities) and D3 (shortfall decided by Planning/Management; no overproduction without a raised ordered quantity) were APPROVED on 2026-10-23 (Decision Log #95); the "[OPEN D2]/[OPEN D3]" markers below are superseded.

> **Implementation status:** the master data of sections 2 and 3 (units of measure, conversions, quantity types with explicit set members) is implemented (QC masters PR, Decision Log #90). Item columns, QC inspection lines and balances (sections 3-6) are not.

## 1. Existing position that this design touches

- [EXISTING] Decision Log #9, Master 20: QC owns configurable units of measure; reciprocal conversions are derived automatically (PAIR to PCS 2 implies PCS to PAIR 0.5).
- [EXISTING] Decision Log #5-#7: QC approval of the preceding stage gates the next; accepted / rejected quantity; permanent rejection is scrap; rework returns through rework handling.
- [DEMO] Decision Log #58, #79: the demonstration approves the **whole stage quantity** only. That is a demo limitation.
- [REQ] #83: QC results are recorded at individual-unit / sub-quantity level. **This supersedes the whole-quantity-only limitation for the future production workflow.** The demonstration remains whole-quantity until the quantity-level QC PR (staged plan 12) replaces it. Nothing here is implemented.

## 2. Unit of measure versus quantity type [APPROVED #90 for ownership and mapping; field design PROPOSED]

| Concept | Answers | Examples | Owner |
|---|---|---|---|
| **Unit of measure (UOM)** | How is a measurable amount expressed? | kg, mm, m, PCS, PAIR, SET | QC |
| **Quantity type** | What countable thing is ordered, produced and inspected? | piece, pair, set | QC |

Proposed relationship: each quantity type defines a **base countable unit** (the smallest thing QC may disposition separately) and the **number of base units per one of the type**: piece = 1, pair = 2. **A set has no standard composition (#90):** each set quantity type carries an explicit member list (`QuantityTypeMember`: member description, base units per set, optional base unit code); two sets are alike only if both lists say so. The base-unit count of a set is the sum of its member counts. UOM conversions (PAIR to PCS 2) are kept for conversions and reporting but the item's quantity type is the authoritative statement of how many base units one ordered unit contains; the two must never disagree (a validation rule when both exist).

Use in:
- **Item definition** (Planning selects from QC's list): quantity type and ordered quantity in that type.
- **Production quantities**: stored in ordered units for display, in base units for balances.
- **QC records**: always in base units.
- **Price calculation**: cost is per base unit and shown per ordered unit [DEFAULT]. Example: a pair priced as 2 x per-piece cost; a half-accepted pair is not a priced pair.

Physical quantities (weight kg, dimensions mm) use UOM only and are not quantity types.

## 3. Entities [PROPOSED]

| Entity | Fields | Notes |
|---|---|---|
| `UnitOfMeasure` | Code, Name (fa), Kind (count/mass/length), IsActive | QC maintains. Existing design (Domain 5.1). |
| `UnitConversion` | FromUom, ToUom, Factor (> 0) | Reciprocal derived. Existing. |
| `QuantityType` | Code, Name (fa), BaseUnitCode, BaseUnitsPerType (integer >= 1; for a set the sum of its members), IsActive, ValidFrom |
| `QuantityTypeMember` (sets only) | QuantityTypeId, MemberNo, Description, BaseUnits | Members are fixed once the type is used by an item. | Retire, never delete once used. Changing BaseUnitsPerType of a used type is forbidden (create a new type). |
| `RequestItem` additions | QuantityTypeId, OrderedQuantity, BaseUnitsOrdered (derived, stored) | Planning/Drawing sets; locked once released. |
| `QcInspection` / `QcInspectionLine` | see section 5 | Lines per disposition and base-unit count. |
| `StageQuantityBalance` | see section 6 | Derived or maintained buckets. |

## 4. Representation of "individual" results - options

| Option | Description | For | Against |
|---|---|---|---|
| A. Unique unit ids / serials | Every base unit has an identity | Exact traceability of which half of a pair | Heavy: ~1000 items with large quantities; operators and QC must label pieces; far beyond stated need |
| B. Lots / batches | Group of units sharing a disposition | Moderate | Introduces lot rules nobody asked for |
| C. **Quantity-level records (recommended)** | QC lines say "n base units: ACCEPTED / REWORK / SCRAP" for an item and stage, each tied to the decision and user | Simplest correct; covers "one of a pair is scrap" as 1 base unit; reconciles by counting | Cannot say *which* physical member; adequate unless the owner needs it |
| D. Optional unit slot on C | Each line may name a slot (e.g. 1 of 2 in a pair) | Adds member identity only where needed | Extra data entry; add only if required |

Recommended default (OPEN D2, not approved): **C**, with D only if member identity is needed. No serials or lots unless a requirement appears.

## 5. QC decision model [PROPOSED]

A QC inspection of a stage (per submission) has lines:

| Field | Meaning |
|---|---|
| InspectionId, StageId, ItemId | Traceability |
| Disposition | ACCEPTED / REWORK / SCRAP |
| BaseUnits | Positive integer count of base units in this disposition |
| Reason code and note | Mandatory for REWORK and SCRAP |
| DecidedBy (QC user), DecidedAt | Responsible user and time |
| SupersedesLineId | A correction creates a new line and keeps the old one |

Stage quantity buckets (derived from the lines plus production submissions):

`Submitted = Accepted + Rework + Scrap + AwaitingDecision` (all in base units).

Rules:
1. **Next stage eligibility:** the next stage may start on `Accepted` base units only (existing gate, now quantity-aware). Rework units are not eligible until they pass QC again.
2. **Rework loop:** rework units return to the same stage (rework handling, existing); after rework they re-enter the stage's `Submitted` as a new submission linked to the original, so they cannot be counted twice.
3. **Scrap is permanent;** it is a loss against the ordered quantity.
4. **Stage status** is a summary: QC_APPROVED when all submitted units are decided and at least the planned eligible quantity is accepted (what "complete" means with scrap is U-19).

## 6. Reconciliation invariants (no double counting)

For each item and stage, in base units:

- I1 `Accepted + Rework + Scrap + AwaitingDecision = Submitted_total` (submissions include rework resubmissions only as *new* units of work, tracked via the `ReworkOf` link, so a reworked unit is in `Rework` once and in the later submission once; the earlier `Rework` line is closed by a resolution row when resubmitted).
- I2 `Accepted(stage n+1 input) <= Accepted(stage n)` accumulated.
- I3 `Accepted(stage n) + Scrap(stage 1..n) <= OrderedBaseUnits` unless over-production is allowed [OPEN D3].
- I4 Scrap at an earlier stage permanently reduces what later stages can receive.
- I5 A correction line never edits the original; the effective disposition is the latest non-superseded line; history stays.
- I6 Daily job reports (R5) cannot change any of these; operators report processed quantity only (JOB section 3). Costing consumes approved QC accepted quantity (COSTING_DESIGN).

Open points ([OPEN D3]; U-20 is settled by the existing #7): shortfall created by scrap (replacement production or accept short delivery); over-production; whether a later QC correction applies to the whole item (the approved rule: send the item back to Stage 1, #7) or only to the affected quantity.

## 7. Supersession handling in the Decision Log

#83 records that individual-unit dispositions are required for the future production workflow. The historical entries #58 and #79 are not erased; they receive a note that the whole-quantity limitation is superseded for the future workflow and the demonstration remains whole-quantity.
