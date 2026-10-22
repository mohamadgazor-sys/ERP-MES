# Costing Design - Machining and Raw-Material Cost (R4)

**Status: DESIGN ONLY. Not implemented. Policies marked PROPOSED are not approved** (register U-22..U-29, U-39). All currency is Iranian Rial; no silent Toman conversion ([EXISTING]).

## 1. Required structure [REQ]

```text
StageMachiningCost      = MachineHourlyRate x ChargeableHours
UnitMachiningCost       = sum over the item's stages of StageMachiningCost (per one item)
UnitRawMaterialCost     = ApplicableMaterialCostPerKg x ItemRawMaterialWeightKg (per one item)
UnitTotalCost           = UnitMachiningCost + UnitRawMaterialCost
TotalCost               = UnitTotalCost x ProducedQuantity
```

Ownership: Planning owns machine rates and stage/item specifications; Warehouse owns raw-material costs; Warehouse cannot edit machine rates; Management approves or changes the resulting price. Calculation inputs and the approved price are auditable.

## 2. Worked example (fictional numbers, for explanation only)

Item with two stages, weight 0.40 kg, steel at 90,000 Rial/kg, quantity 100 base pieces accepted.

| Stage | Machine | Rate (Rial/h) | Approved time attributed to the item (h) | Cost |
|---|---|---|---|---|
| 1 | FCC-06 | 1,200,000 | 25.0 | 30,000,000 |
| 2 | TCC-12 | 900,000 | 12.0 | 10,800,000 |

Machining total = 40,800,000; per piece (accepted 100) = 408,000. Raw material per piece = 0.40 x 90,000 = 36,000. Per-unit total = 444,000. Total = 44,400,000. (Figures are illustrative, not business data.)

## 3. Data flow

1. Operators' daily job reports (JOB doc) carry hours per request/item/stage/machine; supervisors approve them. Only approved lines are cost inputs.
2. QC accepted / rework / scrap base units per stage (QC_QUANTITY_MODEL_DESIGN) define the quantity basis.
3. After the last stage's QC, Warehouse starts the calculation (U-39). The system reads: approved report lines, machine rate effective for each line's work date, item weight, material price effective at the pricing date, quantities.
4. It writes an immutable **calculation snapshot**: inputs (every line with rate id and price id), formulas version, results, calculated by, at. Existing concept: `ItemCostCalculation` with history (Domain 12.5, Master 19.4).
5. Warehouse submits; Management approves or changes the price with a mandatory reason; the original proposal is retained next to the approved price.
6. Warehouse marks the item finished/sent only after approval (U-38).
7. A later QC correction or amended report invalidates the snapshot as "current"; recalculation creates a new snapshot; history stays (Domain 12.5).

## 4. Machining time: actual versus planned [PROPOSED, U-22]

Planned time (#12) is a forecast and priority tool. Cost uses **approved actual job-report time** attributed to the item/stage/machine. Planned time may be shown beside it for comparison and used only for a clearly labelled *estimate* before production. If no approved report exists, no authoritative cost is produced (the calculation is blocked, not defaulted to plan).

Time recording (U-23): minutes stored as integers; hours = minutes / 60 exact (decimal, 6 places); no rounding of time; rounding only on money.

## 5. Rate and price selection [PROPOSED, U-25]

- Machine rate: the rate effective on the **work date of each report line** (a stage that spans a rate change is costed per line).
- Material price per kg: the Warehouse price effective on the item's production-release date (alternative: the date of calculation; owner decides). Both choices are stored on the snapshot.
- Rates and prices are append-only, effective-dated, never overwritten.
- If no rate or price exists for the date, the calculation fails with a named gap; nothing defaulted.

## 6. Policies needing approval

| Topic | Options | Recommendation (not approved) |
|---|---|---|
| Setup time | include in chargeable hours / separate | Included if reported against the stage; no separate setup rate in v1 |
| Overtime hours (U-24) | same rate / premium multiplier | Same rate until the owner supplies a premium; premium would be a Planning-owned, effective-dated factor |
| Downtime | excluded / charged | Reported downtime hours are excluded from chargeable hours (the report line records it separately) |
| Rework hours (U-24) | charge to item / absorb in accepted | Hours spent reworking the same item are included in that item's machining cost, spread over accepted quantity |
| Scrap | cost of scrapped units | Absorbed: all hours and material consumed are divided over accepted units, so per-unit cost rises; scrap cost is also reported separately for visibility |
| Double-count prevention | - | A report line belongs to exactly one item-stage-machine; a unique constraint on (operator, date, time range) and approved-line flag `CostedInSnapshotId`; overtime lines are the same lines as normal ones, never added again |
| Weight (U-26) | nominal weight / actual consumption | Nominal item weight x price; real scrap/offcut factor is not modeled; extra materials only as explicit adjustment lines |
| Quantity basis (U-27) | produced / accepted | Divide by accepted base units; ordered pair/set priced as base units x factor |
| Authoritative figure (U-27) | per-unit / total | Total is authoritative; per-unit is derived; rounding below |
| Rounding (U-28) | per line / final | Round each money line to whole Rial, half away from zero; total is the sum of rounded lines; per-unit = total / accepted shown to whole Rial |
| Adjustments (U-28) | - | Management only, with reason, as separate adjustment lines; originals untouched |
| Contract Make / Repair (U-29) | universal formula / existing routes | Master 19.1: Repair = actual sessions x rates; non-contract Make = material + machine; Contract Make = contract amount. Recommend: formula for Repair and non-contract Make; for Contract Make the formula result is informational |
| Pair/set pricing (U-21) | per pair / per base piece | Per base piece, displayed per ordered unit |

## 7. Permissions

Planning, Management: edit machine rates (Management+Planning, existing). Warehouse, Management: edit material price/kg (existing). Warehouse: calculate and submit costs; Management: approve. Planning: read cost inputs. Nobody edits a snapshot.

## 8. Auditability

Each snapshot stores rate row ids, price row ids, report line ids, formula version, user and timestamps. A reviewer can recompute the result from stored inputs.
