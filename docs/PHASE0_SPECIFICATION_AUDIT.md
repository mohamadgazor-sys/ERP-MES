# ERP/MES — Phase 0 Specification Audit

| | |
|---|---|
| **Date** | 2026-10-09 |
| **Branch** | `claude/erp-mes-phase-0-audit-6y4z44` |
| **Scope** | Documentation audit and implementation-readiness assessment only. No application code, solution, schema, migration or package was created. |
| **Status** | Draft for owner review. **Nothing in this report is an approved decision.** Recommendations are proposals. |

## 0. How to read this report

### 0.1 Files audited

All were read in full. Short names used below:

| Short | File (as found in the repository) |
|---|---|
| **CM** | `ERP_MES_Claude_Starter/CLAUDE.md` |
| **DL** | `ERP_MES_Claude_Starter/docs/DECISION_LOG.md` |
| **SL** | `ERP_MES_Claude_Starter/docs/SESSION_LOG.md` (blank template, no entries) |
| **RF** | `ERP_MES_Claude_Starter/README_FIRST.md` |
| **MS** | `ERP_MES_CSharp_Master_Specification_Current_State.md` (business authority) |
| **AS** | `ERP_MES_CSharp_Architecture_Specification.md` |
| **DS** | `ERP_MES_CSharp_Domain_and_Database_Specification.md` |

`§` references are to section numbers *inside* each document. "MS update" means the closing section "Canonical Business Decision Update — 2026-10-09" of MS, "AS §42" the same-dated block in AS, and "DS §37" the same-dated block in DS.

### 0.2 Classification and IDs

| Class | Meaning | ID prefix |
|---|---|---|
| 1 | Confirmed and consistent | bullets, no ID |
| 2 | Conflicting or outdated | **X**n |
| 3 | Unresolved business decision | **U**n |
| 4 | Technical decision that can safely wait | **T**n |

Each Class 2/3 item says what it is, where, why it matters, and a recommendation. Recommendations are *not* approved.

### 0.3 Rule for "settled" decisions

Decisions that DL, CM or the dated update blocks mark as approved are treated as settled and are **not reopened**. Where an *older passage* still contradicts a settled decision, it is reported as outdated text (Class 2) so the document can be corrected, not as a question about the decision.

Where the *approved text itself* is ambiguous or internally inconsistent (for example X14), that is flagged because an implementer cannot act on it. It asks for clarification, not reversal.

### 0.4 What this audit did not do

- It did not modify any existing file.
- It did not build, run or test anything. No .NET SDK is installed in this environment (`dotnet: not installed`), and a WPF application cannot be built or executed on this Linux host in any case.
- Statements about third-party behaviour (SQL Server collation, EF Core + SQLite limits, .NET and Windows support dates) come from general knowledge and were **not verified in this session**. They are marked *(verify)* and should be checked against current vendor documentation before any decision relies on them.

---

## 1. Executive summary

**The specifications are strong on business intent and settled rules. They are not yet a safe basis for schema freeze beyond a narrow foundation.** The business decisions DL records as approved are applied consistently across MS, AS and DS in nearly every place they appear. Most defects are *stale text* that predates the 2026-10-09 decisions, plus a set of *gaps* in the Domain spec relative to the Master spec.

Counts: 40 conflicting/outdated items (X), 52 unresolved items (U, a few of them technical-leaning), 19 deferrable technical items (T) — plus a short editorial errata list (Appendix B). Item numbers are not contiguous: some numbers were retired while drafting and are intentionally not reused.

### 1.1 Findings that matter most

1. **Repository layout does not match the instructions (X52).** CM tells Claude Code to read `docs/…`, but there is no `docs/` directory. The three specs are at the repository root. `CLAUDE.md` is in `ERP_MES_Claude_Starter/`, so it will not be auto-loaded at the root. The `.claude/skills/` folder named in RF does not exist.
2. **QC and quantity flow is the largest unmodelled area (U14, X13).** The settled rule says accepted quantity continues while rework returns and rejected quantity is scrap. A single `StageStatus` per stage cannot represent "90 pieces waiting at Stage 2, 5 in rework at Stage 1". A quantity/lot model is required before the stage and QC schema. This does **not** block Milestones 1–3.
3. **Stale text contradicts settled decisions in several places.** Examples: MS §14.6 and §39.6 treat rejected quantity as rework; MS §13.1/§13.3 say priority scale and calendar are unspecified; MS §1.3 says contract-rejection routing is "not invented"; MS §28 and Appendix C #10 describe an Excel layout with no Work Type column. These should be annotated as superseded (Appendix B).
4. **QC-correction target is ambiguous inside approved text (X14).** DL #6 and CM say "back to Stage 1". MS §1.10 and §15.1 say "the affected stage (for the confirmed example, Stage 1)".
5. **Case-sensitive RequestNo uniqueness is stated but has no mechanism, and the two target databases behave differently (X45).** SQLite compares text case-sensitively by default; SQL Server's default collations do not. SQL Server also ignores trailing spaces in comparisons *(verify)*, which collides with CM's "do not trim or change case silently". A whitespace policy and an explicit collation strategy are needed before the Request table exists.
6. **The permission matrix covers only 13 sensitive actions (U46).** Default-deny, which CM and DS mandate for unlisted sensitive actions, would block nearly every workflow beyond login. Role names are also undefined (U45).
7. **The Request-level status list and transition table do not exist (U1).** AS §7.1 says transitions "must come from the canonical Master Specification", but MS contains only narrative. A state machine cannot be built or tested without them.
8. **DS is missing entities that MS and DS itself require (X26, X10, X39, X40):** downtime, working calendar and holiday exceptions, route revisions, queue-override batch/detail, stage-unit capability and reassignment, contract decision history, and invalidation/supersede markers on cost and warehouse records.
9. **Windows 10 / .NET lifecycle has moved (T1).** By general knowledge *(verify)*, Windows 10 mainstream support ended on 2025-10-14, and .NET 8 support ends in November 2026. The DL already marks the baseline as unresolved. This affects the choice of target framework at solution creation.

### 1.2 Decisions needing owner approval

Grouped by when they are needed (full list and recommendations in §5):

| When | Decisions |
|---|---|
| **Before Milestone 1 starts** | D1 provisional framework target · D2 repository layout housekeeping · D3 documentation-amendment PR · D4 cross-cutting technical conventions |
| **Before Milestone 2 schema** | D5 request statuses/transitions · D6 role catalogue + provisional permission map · D7 RequestNo key details · D8 Customer master fields · D9 quantity numeric type · D10 who creates/edits items · D11 document link model and file rules |
| **Before Milestone 3** | D12 Excel template, conflict options, failure policy |
| **Before the stage/QC milestone (not blocking M1–M3)** | D13 the list in §5.4 |

### 1.3 Recommended first three milestones

1. **M1 Foundation** — solution, Persian/RTL shell, provider-neutral persistence base, authentication, authorization mechanism, Jalali and Rial primitives.
2. **M2 Customers, Requests, Items** — Customer, UOM, Request (with exact business key), request workflow to the Planning/Drawing hand-off, mixed MAKE/REPAIR items, documents.
3. **M3 Excel import** — header-mapped template, preview/validation, conflict detection and resolution, transactional commit, 1,000-row performance check.

Acceptance criteria and tests are in §6. Scheduling, production stages, QC, contracts, costing and warehouse come after M3.

---

## 2. Repository and process readiness

### X52 — Repository layout does not match CM, RF or AS (Conflicting)

- **Where:** CM "Start-of-session requirements" items 1–3; RF lines 3–9 and 11–15; AS §3 (solution tree shows specs under `docs/`).
- **Found:** no `docs/`; specs at root; CM, DL, SL, RF under `ERP_MES_Claude_Starter/`; no `.claude/skills/`; no `.gitignore`; no CI configuration.
- **Why it matters:** Claude Code loads `CLAUDE.md` from the repository root and from parent directories of the working directory. The instructions that protect the project (default deny, no invented rules, session log) will not load automatically. Every future session's "read these files" step points at paths that do not exist.
- **Recommendation (D2):** in a separate, approved housekeeping change, move the starter files into the layout RF describes (`CLAUDE.md` at root, specs and logs under `docs/`). This audit deliberately did not move or edit anything.
- **Side effect of this audit:** creating `docs/PHASE0_SPECIFICATION_AUDIT.md` creates the `docs/` directory with this one file in it.

### Other process observations

- SL contains only the template. No prior session is recorded, so there is no "latest entry" to read (CM requires one if present). CM requires an entry at the end of every substantial session; this session's entry is deferred because the instructions allow only one new file and forbid changing existing documents. **Recommended next action:** add a session entry as part of the D2/D3 housekeeping change.
- DL lists "request progress percentage" twice under Unresolved (lines 31 and 39). Harmless, but the duplicate should be merged.
- DL #19 says sensitive-action permissions are defined "in the Master Specification and project instructions". They are actually defined in DS §15.2 / §32.8 (MS update says "Domain/Database Specification"). Stale cross-reference.
- There is no `.gitignore`. CM forbids committing credentials, customer PDFs and production database backups. A `.gitignore` for `*.db`, `bin/`, `obj/`, `*.pdf` and secrets should be the first commit of M1 (T-level, no decision needed).

---

## 3. Audit by area

### 3.1 Request, customer, item and work-type relationships

**Confirmed and consistent**
- The system is request-based; Projects are not mandatory and Request → Item → Stage is the canonical chain (DL #2; CM; MS §1.2, §31 #6; AS §2; DS §1, §31 #25).
- Customer requests only in v1; no Internal Request workflow (DL #3; MS §1.1; DS §1).
- `WorkTypeID` (MAKE | REPAIR) is item-level; one request may mix both; `RequestTypeID` is not a work-type indicator (DL #2; MS §1.1, §7; AS §2; DS §2.3).
- Repair-only data lives in a separate one-to-one `RepairItemDetails`; `IsEmergency` is excluded from the C# entity (MS §5.3, §30.3; DS §5.3).
- `(RequestID, ItemNo)` is unique; `ItemNo` is text (MS §28; DS §5.2, §31 #3).
- Emergency, Final-QC and mandatory Project conversion are removed (DL #3; MS §41; DS §31).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X1 | MS lists an item-level `BuyerID` "where item-level buyer differs". DS `RequestItem` has no buyer. | MS §5.3 vs DS §5.1 | A second customer source on an item breaks the Customer+RequestNo+Year identity and contract customer derivation. | Do not model item-level customers unless a real case is stated. Mark the MS line legacy. |
| X2 | Naming: MS uses `BuyerID`/`BuyerName` (§3.1, §23.1); DS/AS use `Customer`. | MS vs DS/AS | Two names for one master invite two tables. | `Customer` canonical; "Buyer" legacy. |
| X3 | Priority, `PlannedStartDateTime`, `PlannedEndDateTime` sit on `RequestItem` in DS §5.1, but on `ItemStage` in MS §30.4 and AS §6.4. DS `ItemStage` (§6.1) has planned dates but no `Priority`. DS §18 warns against duplicate priority fields. | DS §5.1/§6.1/§18 vs MS §30.4 | Schema churn and double sources of truth. | Keep all scheduling columns out of the M1–M3 schema. Decide with U21. |
| X4 | MS says repair data must not "pollute" the general item entity, then DS puts all Make fields (material, weight, supply, SupplyRequestNo/Date, NeedsIncomingQC…) into the shared `RequestItem`. DS §24's diagram shows a "Make Details" box with no entity in DS §3. | MS §5.3, §33.3; DS §5.1, §24 | Inconsistent application of the same design principle; very wide table. | Either add `MakeItemDetails` or state explicitly that Make fields stay in `RequestItem`. For M2, include only identity, quantity, UOM, dimensions and material text. |
| X5 | `DrawingDocumentID` / `Contract.DocumentID` single-FK fields vs. document *link tables* and "store the binary once". | DS §5.1, §5.3, §11.2 vs DS §20, DL #18 | Single FKs cannot express multiple links or contract resubmission with a corrected PDF. | Use link tables only (DS §20 is later and explicit). Remove the single-FK fields. See D11. |

**Unresolved business decisions**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| U1 | No Request status catalogue or transition table. | AS §7.1; DS §4.3; MS App. F (historical, includes obsolete states) | **Blocks M2.** Cannot build or test the request state machine. | Owner approves a minimal catalogue and transition table (illustrative draft in Appendix A). |
| U2 | "Completed item" is undefined, including how scrap affects completion. | MS §12.5; DS §22 | Needed for progress, warehouse eligibility and closing. Not needed for M1–M3. | Decide with the quantity-flow model (U14, U15). |
| U3 | Who creates and edits items: Drawing defines Make items (MS §6.1), Planning defines Repair items (MS §5.3), and Excel import is "Planning/Drawing" (MS §28.1). None of these is in the permission matrix, so default-deny applies. Edit rights after release are also unspecified. | MS §5.3, §6.1, §25.4, §28.1; DS §15.2 | **Blocks M2/M3.** | Provisional map in D6/D10. |
| U4 | Material: Excel supplies `MaterialName` text, DS `RequestItem` has `MaterialID` FK. Unknown material in an import: reject, or create? | MS §28; DS §5.1, §17, §21.1 | Affects whether a Material master must exist before M3. | Recommend: reject unknown (or require explicit mapping), never auto-create. Needs approval (D12). |
| U5 | Customer entity is in the inventory (DS §3) but has no field definition anywhere. | DS §3 | **Blocks M2.** | Minimal master (D8). |

### 3.2 Request numbering and case-sensitive uniqueness

**Confirmed and consistent**
- `RequestNo` is customer-supplied free text, stored exactly; never generated or renumbered; not globally unique; `RequestID` is the internal key (DL #1; CM; MS §7; DS §4.2, §32.1).
- Business key is **Customer + RequestNo + Jalali Request Year**, unique; the comparison is case-sensitive (`wsj-898` ≠ `WSJ-898`); no silent trim or case change (same sources; DS §26 index list).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X6 | DS §4.1 describes `RequestNo` as a "Customer-visible **unique** request number". | DS §4.1 vs §4.2/§32.1 | Direct contradiction of the settled key. An implementer reading §4.1 first would add a global unique index. | Correct the wording. |
| X7 | `JalaliRequestYear` appears only in the DS §26 index list. It is absent from the Request field list (DS §4.1) and from MS §3.1. | DS §4.1 vs §26 | The unique key references a column that is not defined. | Add a persisted `JalaliRequestYear` column. |

**Unresolved business decisions**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| U7 | Which date defines the "Jalali Request Year": customer's request date, date received by Central, or creation date? Can it change if `RequestDate` is edited? | MS §7; DS §4.1–4.2 | **Blocks M2.** An edit could silently re-key a request or break uniqueness; requests near Nowruz are sensitive. | Recommend the customer's request date (`RequestDate`), with year fixed at creation and changes allowed only through an audited correction that re-checks uniqueness. Needs approval. |
| U8 | Whitespace and character policy. CM says "do not trim/change case silently". Not stated: leading/trailing spaces, internal spacing, Persian/Arabic letter variants (ی/ي, ک/ك), digit shapes, ZWNJ. | CM; MS §7 | **Blocks M2.** Under "exact" semantics two visually identical values can both be accepted; under ANSI padding semantics (SQL Server) *(verify)* `'P-565'` and `'P-565 '` compare equal while SQLite keeps them distinct (see X45). | Recommend: **reject** (validation error, never auto-fix) leading/trailing whitespace; otherwise store exactly; apply no letter/digit normalisation to `RequestNo`; optionally warn when a case-insensitive near-duplicate exists. Needs approval. |
| U9 | Is a "near-duplicate warning" (e.g., `WSJ-898` when `wsj-898` exists) wanted? | — | UX only; rule already permits both. | Optional; include in D7. |

### 3.3 Contract workflow and production release gates

**Confirmed and consistent**
- Contract requirement is request-level and covers the whole request (DL #4; CM; MS §18.2; AS §13; DS §11.1).
- Sequence: Drawing confirms doability → Planning decides → Planning creates the contract and PDF → Management approves or rejects (optional explanation) → rejected contracts are corrected and resubmitted → approval is shown on Planning and Warehouse dashboards (DL #4; MS update; AS §13, §39.1; DS §11.1).
- Missing or unapproved required contract blocks production release (MS §1.3, §18.2; DS §31 #15).
- Contract customer is derived through `Request.Customer`; no `Contract.CustomerID` (DL #4; DS §11.2, §32.9).
- Search by contract number, date and customer for Planning, Management and Warehouse (MS §18.5; AS §13.2; DS §11.3).
- Contract value calculation is not invented (MS §18.6, §42).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X9 | MS §1.3 (last paragraph before "Contract document storage") says the downstream action after rejection "is not invented here". Settled decision is correct-and-resubmit. | MS §1.3 vs DL #4, MS update, AS §13, DS §11.1 | Stale, would mislead an implementer. | Annotate as superseded. |
| X10 | DS §11.2 holds a single decision block on `Contract`. "Rejection retained as history" (AS §39.1) plus resubmission needs either one row per submission or a decision-history table. MS §18.3 had `ContractYear`/`ContractSequence`/`IsCurrent`; DS drops year/sequence. AS §6.1 says "zero/one active Contract". | DS §11.2 vs AS §6.1/§39.1; MS §18.3 | A resubmission would overwrite the earlier rejection. | Model `Contract` + `ContractSubmission`/`ContractDecision` history, or versioned rows with `IsCurrent`. Decide at the contract milestone. |
| X11 | Contract PDF as single FK vs link table (see X5). | DS §11.2 vs §20 | Same as X5. | Same as X5. |

**Unresolved business decisions**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| U10 | `ContractRequired` is a boolean in the Request field list. Until Planning makes the determination, "false" is indistinguishable from "not yet checked", so a request could pass the gate un-assessed, contradicting "every customer request must pass a contract-determination check". | MS §1.3, §18.2; DS §4.1 | Core gate integrity. | Recommend tri-state (Undetermined / Required / NotRequired) and require "determined" before release. Needs approval. For M2, **omit the column** and add it in the contract milestone. |
| U11 | Can `ContractRequired` change after approval or after release? By whom? | — | Could reopen or bypass the gate. | Decide at the contract milestone. |
| U12 | `ContractNo`: entered by Planning (customer's contract number?) or app-generated (MS §18.3 had year+sequence)? | MS §18.3; DS §11.2 | Affects numbering and uniqueness. | Ask at the contract milestone. |

**Can wait (Class 4):** T2 — notification mechanism for "approved contract" (persistent unread notifications vs alerts derived from state) (DS §11.4 already marks this as a technical/UI decision). Contract `EXPIRED` status stays reserved.

### 3.4 Production stages, QC approval, rework and external supply

**Confirmed and consistent**
- Each item has its own ordered stages (MS §1.4, §8.3; DS §2.4).
- QC is stage-based; QC approval of stage N−1 gates stage N; no Final-QC (DL #3, #5; CM; MS §1.4, §14; AS §7.3; DS §9).
- Planning releases an internal first stage; an external first stage waits for arrival/return and QC (DL #5; MS §1.5, §10.1–10.2).
- External supply is a stage with dispatch/arrival tracking and QC; external return is not final warehouse receipt (DL #5; MS §6.5, §16–17.3; DS §10).
- Partial QC: accepted continues; **permanently rejected = SCRAP**; only rework quantity returns (DL #7; MS update; AS §8, §39.1; DS §9.3, §32.7).
- QC correction keeps the original inspection, links the correction, marks the item, sends it back, and handles downstream stages manually (DL #6; MS §1.10, §15; AS §14.3; DS §9.4).
- Transactional QC approval (MS §36.2; AS §22; DS §27).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X13 | MS §14.6 ("rejected quantity enters the rework path"), MS §39.6 ("5 remain rejected according to the defined QC outcome") and AS §7.2 ("Rejected → External correction/rework route") predate the SCRAP rule. | MS §14.6, §39.6; AS §7.2 vs DL #7, DS §9.3 | Acceptance test 39.6 as written cannot be passed against the settled model. | Rewrite 39.6: 90 advance, 5 return as rework, 5 recorded as scrap. Annotate §14.6 and AS §7.2. |
| X14 | QC-correction target: DL #6, CM and DS §9.4 say the item returns to **Stage 1**. MS §1.10 and §15.1 say "the affected stage (for the confirmed example, Stage 1)". MS App. C #7 refers to "the confirmed rare Stage-1 correction scenario". | as listed | If QC corrects the Stage-3 decision, does the item go to Stage 3 or Stage 1? These readings produce different workflows. | Ask the owner to confirm the exact rule in one sentence. (Not a reopening: the approved text conflicts with itself.) |
| X15 | Stage-status vocabularies differ. MS §10 and App. F.7 list ~24 states (including `DELIVERED_TO_QC`, `QC_RECEIVED`, `UNDER_QC_INSPECTION`, `PAUSED`, `WAITING_MATERIAL`, `WAITING_DRAWING_REVISION`). DS §7 drops several, adds `CANCELLED`, and merges `QC_REJECTED / REWORK_REQUIRED`. Repair items also carry a separate status chain (MS §5.5), though §5.5 asks for "one coherent vocabulary". | MS §10, §5.5, App. F vs DS §7 | Three overlapping state machines (request, repair-item, stage) with no defined item-level state. | One approved catalogue with category (active / blocked / terminal) and stable codes (MS §43.12) before the `ItemStage` schema. |
| X16 | First external stage: on release a dispatch is auto-created and the stage is `EXTERNAL_SENT` with no physical send. A later external stage waits in `WAITING_EXTERNAL_SEND` until Warehouse registers the send. The dispatch row has `SentAt`/`SentQty` (suited to contractor processing) but external *supply* (a purchase) has nothing to send, only an expected arrival. | MS §10.2, §10.4, §16.3–16.5, §6.5–6.6; DS §10.1 | The same event model is used for two different business processes with inconsistent first-stage vs later-stage behaviour. | Decide whether auto-created dispatch means "planned" or "sent", and whether Supply and Processing need distinct fields (`ExpectedArrival` vs `SentAt`). |
| X17 | The prototype `NeedsIncomingQC` flag (and supply types 1–2 with "Incoming QC = False", and a "Manager approval" flag on supply types 4–5) remain in MS §6.2/App. F.4 and DS §5.1, while the settled model requires QC on every stage including arrivals. The "Manager approval" flag has no home in any current workflow. | MS §6.2, App. F.4; DS §5.1 vs DL #5 | Possible hidden bypass of QC or an extra approval step nobody defined. | Declare both flags obsolete, or define them. Do not carry prototype IDs/flags into the schema (MS F.4 already says so). |

**Unresolved business decisions**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| U14 | **Quantity-flow model.** Does Stage N+1 start on accepted pieces while the rework of the rest is still at Stage N? AS §7.3 allows "partial quantities may proceed independently where supported"; DL #5/MS §14.5 gate on stage-level approval. | AS §7.3, §8; MS §14.4–14.5, §20; DS §6.1, §9.3 | One status per stage cannot represent split quantities. This is the most consequential open schema question. **Blocks the ItemStage/QC schema, not M1–M3.** | Owner decides between (a) whole-stage gating and (b) lot/quantity-ledger flow. Recommend (b)-capable design (a stage-quantity ledger) once decided. |
| U15 | Shortfall after scrap: is replacement production planned? What makes an item "complete" if accepted < requested? | MS §14, §20; DL #7 | Completion, progress and warehouse receipt all depend on it. | Decide with U14. |
| U16 | Rework mechanics: returns to the same stage and unit? Is only the rework quantity re-inspected? | MS §14.6; DS §9.3 | Needed for the status model and queues. | Decide with U14. |
| U17 | External stages: on QC rejection, is the quantity scrap (settled rule) or returned to the supplier/contractor for replacement (MS §6.5 "supplier-correction process"; App. F.5–F.6)? Partial returns and multiple shipments per stage? Dispatch cardinality per stage (MS §43.15 wants duplicates prevented)? | MS §6.5, §16.3, §43.15; AS §7.2; DS §10.1 | `ExternalWorkDispatch` has a single `ReturnedAt`. | Decide with U14. |
| U18 | Route revision: effect on already-created downstream stages after inserting a stage; may a stage be inserted before an already QC-approved stage? | DL; MS update; AS §39 #3; DS §6.2, §32.4 | DS itself calls it a "blocking subdecision". | Decide before the stage milestone. |
| U19 | Alternative units, reassignment history and "who assigns machines, and when" appear in MS §9.1 but no DS entity exists (`StageUnitCapability`, `StageReassignment`). | MS §9; DS §3 | Queue and scheduling depend on it. | Add to DS when the stage milestone is planned. |
| U20 | QC correction details: direction (approve→reject, reject→approve), item already received into the warehouse, clearing the correction marker, roles allowed within QC. | MS §15; DS §9.4, §12.5 | Edge cases around the settled rule. | Decide before QC correction is built. |

**Can wait (Class 4):** T3 — status as lookup table vs. code enum plus lookup; keep stable codes. Decide after U14/X15.

### 3.5 Planning priority, management overrides and scheduling

**Confirmed and consistent**
- Priority labels exactly `عادی`, `فوری`, `خیلی فوری`; multiple items/stages may share a value (DL #11; CM; MS update; AS §39.1/§42; DS §18).
- Planning owns normal priority and order; the live queue reflects its changes (same sources).
- Management alone may override queue order in a **batch**, with per-row audit of old/new order, manager, timestamp and reason; Planning's underlying priority is preserved (same sources; DS §18.1).
- Load = minutes per piece × planned quantity; external stages also have expected lead time (DL #12, #15; MS §13.2; AS §9.1; DS §19).
- Planned and actual values are kept separate; planned end ≠ actual completion (MS §13.3; AS §9.1; DS §6.1, §19). DS §19.3: until the algorithm is final, only a clearly-labelled *estimated* end date may be shown.

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X19 | MS §13.1 ("priority scale is not specified") and §13.3 (calendar "not specified") are superseded by DL #11/#13 and the MS update. | MS §13.1, §13.3 | Stale. | Annotate. |
| X20 | DS §32.2 says priority codes are "ordered from lowest to highest"; DS §18's table sorts `VERY_URGENT = 1 (highest)`. | DS §18 vs §32.2 | Ambiguity about what `SortOrder` means. | Say explicitly whether lower number = higher urgency. |
| X21 | The matrix lets Planning *or Management* "change priority", noting Management edits "must not be confused with the explicit override". DL #11 and MS say Planning owns normal priority and Management alone overrides. If Management edits priority directly, it is unclear whether Planning's "underlying priority" is overwritten. | DS §15.2 vs DL #11 | Could erase the very value the override is meant to preserve. | Record every direct priority change (actor, old/new) and keep it distinct from the override layer. |
| X22 | Ordering rule: MS §12.2 / AS §10.2 sort by status class first (in-progress, stopped, rework, waiting). Priority and overrides apply "within" those classes. | MS §12.2; AS §10.2 | A "very urgent" job that is waiting appears below an ordinary job in progress, and a Management override cannot lift it above one. Possibly intended; the documents never say. | Confirm intended precedence (U24). |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U21 | Priority granularity: `RequestItem` or `ItemStage` (DL; DS §18, §32.2). | — | Decide before the scheduling milestone; keep out of M1–M3. |
| U22 | Tie-break among equal priorities; DL says store "planning order as a separately stored position". | DL; AS §39 #1 | Same milestone. |
| U23 | Precedence between a later Planning change and an active Management override (fixed / flagged for review / recalculated). | DL; AS §39 #1, §42; DS §18.1 | DS requires that both facts be preserved with no silent loss. |
| U24 | Status class vs priority vs override precedence (X22). | — | Owner confirmation. |
| U25 | Capacity allocation: multiple eligible machines, no machine assigned yet, parallel work, setup/batch time, times that vary with quantity, interchangeable machines, external lead time in calendar vs working time. | DL; AS §39 #2; DS §19.3 | Keep scheduling "estimate-only" (DS §19.3) until decided. |
| U26 | Precision of minutes-per-piece and quantities (fractions?). | — | Decide with D9. |

**Can wait (Class 4):** T4 — scheduling-engine design and recalculation triggers; T5 — dashboard refresh strategy (polling vs push) (DS §32.12, AS §39 #9).

### 3.6 Machine downtime and configurable working calendars

**Confirmed and consistent**
- Saturday–Thursday workdays; Friday off by default, but approved work/extra hours are possible and must be recorded (DL #13; CM; MS update; AS §9.3; DS §19.1).
- Current shift 08:00–17:00 with a 12:00–13:00 break (8 net hours); one-, two- (06:00–14:00 + 14:00–22:00) and three-shift patterns configurable; breaks per shift not hard-coded (same sources).
- Planning maintains holidays/off-days (DS §15.2 row; DS §32.8).
- PM reports downtime (including maintenance and power outage) to Planning; downtime reduces capacity and moves planned ends; it is auditable (DL #14; MS §21; AS §9.3; DS §19.2).
- Calendar changes affect future calculations only (DS §19.1).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X24 | Department lists differ. MS §26 has no Maintenance/PM department; DS §15.3 adds it. No source defines how an "Admin" fits. | MS §26 vs DS §15.3 | Users must belong to exactly one department. | Settle the department list (D6). |
| X25 | Downtime approval. MS §21 has `ApprovedByUserID` and defines available time as "shift time − **approved** downtime"; DL #14 says "approved/recorded"; DS §15.2 calls the approval workflow "a technical configuration item"; DL lists "how downtime is approved" as unresolved. | MS §21; DL #14; DS §15.2 | A business rule is being treated as configuration. | Treat as a business decision (U27). |
| X26 | DS entity inventory (§3) lacks `MachineDowntime`, working calendar / holiday / exception entities, shift-pattern and break entities (only `ShiftType`), and the recorded Friday/extra-hour approvals that DS §19.1–19.2 require. | DS §3, §19 vs MS §21 | The Domain spec cannot yet generate this part of the schema. | Add the entities to DS before the scheduling milestone. |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U27 | Who approves downtime, and do unapproved records count provisionally? | MS §21; DL | Owner decision. |
| U28 | Who is the "supervisor" authorising Friday/extra hours; not in the matrix, so denied by default. | DL #13; DS §19.1 | Owner decision. |
| U29 | Calendar scope: one factory calendar, or per unit/machine? Do external routes follow the factory calendar? | DL (external lead time in calendar dates) | Owner decision. |
| U30 | Holiday source. Religious holidays shift each year; the spec assigns maintenance to Planning. | DS §15.2 | Recommend manual entry by Planning with no automatic import. Confirm. |

**Can wait (Class 4):** T6 — calendar/shift configuration UI.

### 3.7 Excel import, validation, duplicate and conflict handling

**Confirmed and consistent**
- Core v1; up to ~1,000 items; one mixed worksheet; Persian headers; explicit Work Type column that is never inferred (DL #16; CM; MS update; AS §12.4; DS §21).
- Preview and validate before commit; row-level errors identify the Excel row and reason; summary counts; batch commit in a transaction (MS §28.1; AS §12; DS §21.1, §27).
- Duplicate key is `RequestID + ItemNo`; `ItemNo` stays text (`1.10`, leading zeros) (MS §28; DS §21.3).
- Identical rows are not duplicated; changed rows are conflicts requiring manual choice, shown beside incoming values; no silent overwrite and no reset of stages or history (DL #16; MS update; AS §12.2; DS §21.3).
- The Access A:N positional layout is **historical reference only**; the C# template maps by header name (DS §21.2).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X28 | MS §28 still presents the positional A:N table as the "current documented mapping" with no Work Type column; MS App. C #10 says the Work Type column "must be explicitly defined". MS §39.16 only tests the "add new items" case. AS §12.2 repeats the positional table. | MS §28, §39.16, App. C #10; AS §12.2 vs DS §21.2, DL #16 | Misleading to an implementer. | Annotate as legacy; add a conflict scenario to §39.16. |
| X29 | Partial vs all-or-nothing import. MS §28.1 #9 "transaction… where practical", yet #11 reports "rows rejected with errors" (suggesting partial acceptance). AS §12.3 says partial import only "if later approved". DS §21.3/§27 require conflicts resolved before commit. CM says "transactional import". | MS §28.1; AS §12.3; DS §21.3, §27; CM | Cannot implement the commit path without knowing whether one bad row aborts everything. | Recommend all-or-nothing for *errors* (nothing is written until the file is clean); explicit per-row resolution only for *conflicts*. Needs approval (D12). |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U32 | Final Persian header strings, accepted Work Type values (e.g. ساخت / تعمیر), the full column set (Repair-specific columns, UOM, supply type, material), which are mandatory. | DL; DS §32.5; AS §39 #4 | **Blocks M3.** Owner supplies the template. |
| U33 | Set of conflict choices (keep existing / replace / cancel / field-level merge) and the rule when the existing item already has released or started stages. | DL; AS §39 #4; DS §21.3 | Recommend M3 offers only **keep existing** and **cancel import**, plus **replace** only where the item has no stages (all items in M3 have none). Anything touching released items stays blocked. Needs approval. |
| U35 | Unknown UOM / Material / SupplyType values: reject or create? | DS §21.1 | Recommend reject. |
| U36 | Comparison rules for `ItemNo`/`PartName` across Persian/Arabic letter and digit variants, whitespace, ZWNJ. Excel also stores `1.10` as the number 1.1 when the cell is numeric, and drops leading zeros. | MS §28.1 #5 | Recommend: no silent normalisation; the template formats the column as text; the importer reports numeric-typed `ItemNo` cells as errors. |
| U37 | Who may import, into which request statuses; duplicate `ItemNo` rows *within one workbook*; maximum file size/rows. | MS §28.1 #1; DS §15.2 | Default-deny until stated. |
| U38 | A re-import workbook that omits items present in the database. | MS §28.1 #7 | Recommend: never delete or deactivate; report as "not in file". |

**Can wait (Class 4):** T7 — spreadsheet library choice (check licence terms, ability to stream ~1,000 rows, merged-cell support for legacy files); T8 — batch insert technique.

### 3.8 Jalali dates, Rial currency and Persian RTL interfaces

**Confirmed and consistent**
- All user-visible dates are Jalali with 24-hour time; input via Jalali-aware controls; Gregorian never the normal user-facing form (DL #8; CM; MS §0.6, §27; AS §18.2; DS §31 #21).
- Storage is native `DateTime`; Jalali ranges are converted to date ranges before querying; formatted strings are never compared; Jalali year extraction is required (same sources).
- All money is Iranian Rial; never silently Toman; stored numerically; formatting is presentation (DL #8; MS §0.6, §19.0; AS §18.3; DS §12).
- Persian, RTL UI; English technical names allowed (MS §0.4, §37.1; AS §18.1).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X31 | AS §18.2 places `JalaliDateService` as an application responsibility, but the Request business key (Jalali year) is a Domain invariant. | AS §18.2 vs DS §4.2 | The Domain would either depend on a library or duplicate the logic. | Define a pure calendar abstraction (value object / small interface, no dependencies) at the Domain boundary, with any library behind it. |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U39 | Digit shapes: Persian (۰–۹) or Latin on screen? Accept both on input? | MS §37 | Decide before UI work; recommend accept both, display Persian. |
| U40 | Rial rounding and storage type (whole Rial? rounding per work session or on the total?). | MS §19; DS §12 | Decide before costing/rate tables. |
| U41 | Persian text search and sorting (ی/ي, ک/ك, ZWNJ) for names and descriptions, while `RequestNo` stays exact. | — | Store exactly; add a separate normalised search key for non-key fields. |
| U42 | Reports, printing and export formats are mentioned (MS §27) but not specified. | MS §27, §28 (AS reporting list) | Needed before reporting milestones. |

**Can wait (Class 4):** T9 — time-zone and clock source policy (MS §27 says not decided); T10 — Jalali library choice (verify leap-year behaviour with golden tests); T11 — Jalali date-picker control (WPF has none built in; third-party vs custom, licence); T12 — Persian font and its licence; T13 — bidi rendering of LTR identifiers (`P-565`, `1.10`, `wsj-898`) in RTL grids, reports and PDFs (include a spike in M1).

### 3.9 User authentication, departments, roles and permissions

**Confirmed and consistent**
- Local username/password; one department and one role per user; password hashes only (DL #19; CM; MS §0.5, §25; AS §17; DS §15.1).
- Authorization must be enforced at the application-service boundary, not only by hiding UI (MS §25.3; AS §17.2; DS §15.2).
- The 13-row sensitive-action matrix in DS §15.2 / §32.8 agrees with CM's list of who may do what, with one exception: X34.
- Unlisted sensitive actions are blocked until defined (DS §15.2; AS §39 closing note).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X34 | Initial request approval requires **Department = Management AND Role = Manager**; contract approval and queue override require **Department = Management** only; CM and MS §25.4 just say "Management". | DS §15.2, §32.8 vs CM; MS §25.4 | Role names are not defined, so it is unclear who qualifies. | Settle the role catalogue (D6) and state the role requirement for each row. |
| X35 | "Admin user / ADMIN authorization" (CM, DS) vs. every user having exactly one department and one role; no Admin department or role is defined. | CM; DS §15.2, §15.3; MS §26 | Cannot seed the first account. | Define Admin as a role, and decide its department. |
| X36 | DL #19 points to the wrong location for the permission matrix. | DL #19 vs DS §15.2 | Stale cross-reference. | Fix. |
| X37 | Department lists differ across MS §26, DS §15.3 and CM ("Production Units", "Central", "Maintenance/PM", "Admin"). | as listed | Seed data would be inconsistent. | Single list (D6). |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U45 | Role catalogue is undefined (MS App. C #1). | MS | **Blocks M2 seeding.** |
| U46 | No permission is defined for most core actions: create request, submit to Management, define items, import Excel, define stages, release production, record work sessions, deliver to QC, receive in QC, register dispatch/return, cost calculation and approval, warehouse inquiry response, downtime entry, Friday approval. MS §25.4 gives examples but says the matrix must come from the "final company-approved role list". | MS §25.4; DS §15.2 | Propose a **provisional, department-level map derived from MS §25.4** for development only (D6). Everything else stays denied. |
| U47 | Data-visibility scope: MS §25.4 says unit users "view their unit queue", but nothing links a user (or operator) to a production unit. | MS §25.4; DS §14.4, §15.1 | Add to the model when queues are built. |
| U48 | Do floor operators log in, or do supervisors enter work on their behalf? (`Operator.UserID` is optional.) | DS §14.4 | Decide before work-session entry. |
| U49 | Password policy, lockout, reset, first-admin bootstrap (no default password in Git), session timeout, login auditing. | AS §17.1, §35 | Policy for M1; algorithm itself is Class 4 (T14). |
| U50 | Separation of duties (same person creating and approving a contract/cost)? | — | Only if the company wants it. |

**Can wait (Class 4):** T14 — hashing algorithm and parameters (decide inside M1 with an ADR; a vetted built-in or well-reviewed implementation).

### 3.10 Warehouse transactions, costing and historical records

**Confirmed and consistent**
- Warehouse v1 = inquiry, receipt, movement history; no stock balance and no deduction (DL #17; MS §1.8, §17; AS §15; DS §13).
- Duplicate final receipt is prevented; external return is not a receipt (MS §17.2–17.3; DS §13.2).
- Ownership: Planning → machine rate/hour; Warehouse → material price/kg; QC → UOM (DL #10; MS Master Data table; DS §12.1). The matrix extends rates to Management (DS §15.2); MS explicitly left that hierarchy open (MS Master Data note).
- Formulas: `MachineCost = Σ(WorkedMinutes/60 × rate)`, `RawMaterial = Weight × PricePerKg`; effective-dated rates; calculation snapshots the rate used (MS §19; AS §16; DS §12).
- History is never deleted; transactional records use inactive/superseded states, not physical delete (MS §31 #21; DS §25.3).
- Atomic outcomes for QC, external return and warehouse receipt (MS §36; DS §27).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X39 | After a QC correction, prior cost approval and warehouse receipt "must no longer be treated as current truth", but `CostCalculation` and `WarehouseTransaction` have no `IsCurrent`, `SupersededBy` or reversal fields. | MS §1.10, §15.1; DS §12.5 vs DS §12.4, §13.2 | The settled rule cannot be represented. | Add invalidation markers and a reversal transaction type; decide mechanics at the warehouse/cost milestone. |
| X40 | The rate must be snapshotted per work session ("each work session may use its own machine rate"), yet the per-session detail table is a backlog item and the aggregate row has one `MaterialPriceRateID` and total machine cost only. | MS §19.5, §19.7; DS §3, §12.4 | The snapshot rule cannot be satisfied or audited from the aggregate alone. | Make per-session cost lines part of the first costing milestone, or snapshot rate on the work session. |
| X41 | Material price source: item-level `MaterialPriceRateID` (MS §6.2; DS §5.1) vs "applicable" price at calculation time (DS §12.4). | as listed | Two competing sources. | Decide which; recommend rate resolved at calculation and snapshotted. |
| X43 | Receipt granularity: MS §17.2 has two routes (repair receipt, stage receipt); DS has one `WarehouseTransaction` with `Quantity` and optional `ItemStageID`. | MS §17.2; DS §13.2 | Duplicate-receipt prevention needs a defined grain. | Define: per item, per final-stage quantity, partial receipts allowed or not. |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U51 | Cost scope: `TotalCost = RawMaterial + Machine` ignores external/outsourced processing and purchased supply (casting, ceramic, plating). Do rework/scrap sessions count? | MS §19.1, §19.4 | Business decision before costing. |
| U52 | Contract-Make items: MS says the contract route is "authoritative" but contract value logic is excluded, so no step exists between QC completion and warehouse release. | MS §18.6, §19.1, §19.6 | Decide what gates warehouse release for contract items. |
| U53 | Reversal of a warehouse receipt after a QC correction. | MS §1.10; DS §12.5 | Decide before the warehouse milestone. |
| U54 | Who may create and answer warehouse inquiries? (MS §17.1: "Drawing/Planning creates".) Not in the matrix. | MS §6.4, §17.1 | Default-deny until stated. |
| U55 | Management's role in cost: what `ManagementFinalCost` represents (customer price?) and who may view it. | MS §19.4 | Clarify at the costing milestone. |

**Can wait (Class 4):** T15 — retention and hard-delete policy (DL; DS §25.3, §33): default to no physical deletes; T16 — detailed cost reports and warehouse movement reporting.

### 3.11 SQLite development and later SQL Server migration

**Confirmed and consistent**
- SQLite is for temporary single-user development; SQL Server is the final production backend after approval; business logic must stay provider-independent (DL #20; CM; MS §0.3; AS §4, §33; DS §28).
- Provider is chosen through configuration; SQLite-specific SQL stays out of Application services (AS §4.3; DS §28).
- Document binaries live in the database; the SQL Server storage form (`varbinary(max)` vs FILESTREAM) is open and deferrable (AS §39 #10; DS §20.1).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X45 | **Case-sensitive RequestNo is stated as a rule but no mechanism is given, and the providers differ.** SQLite compares text case-sensitively by default. SQL Server's default collations are case-insensitive, and it ignores trailing spaces in comparisons *(verify)*. A unique index created naively in SQL Server would reject `wsj-898` vs `WSJ-898` and treat `'P-565'` and `'P-565 '` as equal. | DS §4.2, §26 vs DS §28 | Passing tests on SQLite could hide a production failure that only appears after migration. | Specify an explicit binary/case-sensitive collation for `RequestNo` on both providers, reject edge whitespace (U8), and run a uniqueness test on SQL Server before M2 is accepted. |
| X46 | AS §22 requires "concurrency-safe from the beginning" but gives no mechanism. SQLite has no native row-version type, and SQL Server does. | AS §22 | Without a portable token, optimistic concurrency is an afterthought. | Add an application-managed version column to mutable entities from M1 (D4). |
| X47 | The "same business model on both providers" intent needs a provider-neutral subset. Known SQLite differences *(verify against current EF Core docs)*: limited `ALTER TABLE` (EF rebuilds tables), no native decimal (affects SUM/ORDER BY translation), weak `DateTimeOffset` support, foreign keys must be enabled per connection, limited index features. | AS §4.2; DS §28 | Late discovery of incompatibilities is expensive. | Write the neutral subset down (D4) and add a SQL Server test run before the stage milestone, not at the end of the project. |
| X48 | Migrations are provider-specific in EF Core *(verify)*, while AS §33 treats creating SQL Server migrations as a late step. | AS §33 | Affects project structure from M1. | Decide: separate migration set per provider, and whether the SQLite dev database is disposable. |

**Unresolved business decisions**

| ID | Finding | Source | Recommendation |
|---|---|---|---|
| U57 | SQL Server version, edition, hosting, backup/restore, database collation. | DL | Required before production, not now. |
| U58 | Must SQLite development data ever be migrated to production? | AS §33; MS §40 | Recommend "no": dev data is disposable; only reference/master data is seeded. |

### 3.12 WPF application structure and testing strategy

**Confirmed and consistent**
- Four source projects (Desktop, Application, Domain, Infrastructure) plus Domain/Application/Infrastructure test projects; dependencies point inward; business rules live outside code-behind (AS §3, §5, §7).
- Use cases enforce authorization and transactions; dashboards use read models and database-side aggregation; no per-item queries; paged/virtualised lists; async with cancellation (AS §20–21; CM).
- Testing levels: domain, application, database/integration, UI (AS §31); acceptance scenarios in MS §39.
- Incremental build order with a build and test after each milestone (AS §38, §40; CM).

**Conflicting or outdated**

| ID | Finding | Source | Why it matters | Recommendation |
|---|---|---|---|---|
| X49 | AS §3 lists no UI-test project though AS §31.4 requires UI tests; the docs list omits the Domain spec; Jalali and Document services have no named home. | AS §3 vs §31.4, §18.2, §19 | Gaps in the first solution layout. | Add a UI/manual-checklist plan and name homes for calendar, money and document abstractions. |
| X50 | AS §38's "first milestone" bundles 12 items, including a request workflow engine, authentication and Excel import, which depend on U1, U45–U49 and U32–U37. | AS §38 | Too large and blocked by undecided items. | Split into M1–M3 (§6). |

**Unresolved (technical-leaning, can be decided by the team)**

| ID | Finding |
|---|---|
| T17 | MVVM helper library, dependency injection, logging, resource strategy for Persian strings, navigation, theme, third-party controls. |
| T18 | Test frameworks, UI-automation approach for RTL WPF, golden-data fixtures for Jalali, performance targets (the owner must supply numbers), CI runner. **WPF requires a Windows build agent; the Linux sandbox used for this audit cannot build or run it.** |
| T19 | Packaging and update mechanism (AS §34 says not specified). |
| U59 | Performance targets for 1,000-item screens and import: owner to state acceptable times. |

---

## 4. Platform baseline (cross-cutting)

- **Settled:** Windows 10/11 only, no Windows 7/8; WPF/MVVM; modern .NET; the older Windows 7 machines are to be migrated (AS §1).
- **Unresolved (already recorded in DL):** minimum Windows 10 release; exact .NET target; SQL Server version.
- **T1 — lifecycle risk:** by general knowledge *(verify at lock-in)*, mainstream Windows 10 support ended on 2025-10-14, and .NET 8 reaches end of support in November 2026. A newer long-term-support .NET release is therefore the safer provisional choice for a project starting in October 2026. Factory hardware that cannot run Windows 11 would remain on an unsupported or paid-extended-support Windows 10. This is a deployment and security risk the owner should know about, not a reason to delay M1.
- **Recommendation (D1):** approve a *provisional, non-binding* development target (latest LTS .NET, `-windows` TFM for the Desktop project only, plain TFM for Domain/Application/Infrastructure) and require re-validation on real factory machines before any production commitment. This unblocks M1 without pre-empting the DL's "confirm before locking" instruction.

---

## 5. Decisions requiring owner approval

Nothing below is approved. Each item gives a recommendation to react to.

### 5.1 Before Milestone 1 starts

| # | Decision | Recommendation |
|---|---|---|
| D1 | Provisional framework target (§4). | As above; mark it provisional in DL. |
| D2 | Repository layout (X52). | Housekeeping PR: `CLAUDE.md` at root; specs and logs in `docs/`; add `.gitignore`; first SL entry. |
| D3 | Documentation amendments listed in Appendix B and the X-items flagged "annotate/correct". | One documentation-only PR. Supersede notes rather than silent rewrites; update DL when you approve. |
| D4 | Cross-cutting technical conventions (X46–X48, T9, T14): integer surrogate keys (DS §25.1); application-managed version column; UTC for timestamps and a date-only type for calendar dates; provider-neutral subset; one migration set per provider; generic append-only audit table for master-data and configuration changes alongside the domain-specific histories DS already requires; hashing algorithm. | Record as short ADRs in M1. |

### 5.2 Before Milestone 2 schema

| # | Decision | Recommendation |
|---|---|---|
| D5 | Request status catalogue and transitions (U1), including whether a rejected request can be reopened. | Start from Appendix A. |
| D6 | Department and role catalogue (U45, X24, X34–X37) and a **provisional** department-level permission map for actions outside the 13 rows (U46). | Development-only map from MS §25.4; all else denied; revisit before production. |
| D7 | RequestNo details: source of Jalali year (U7), whitespace and normalisation (U8), near-duplicate warning (U9), collation (X45). | See U7, U8. |
| D8 | Customer master minimum fields (U5, X1, X2). | Identifier, name, active flag, audit; no item-level buyer. |
| D9 | Quantity and minutes-per-piece precision (U26). | Decimal with a fixed scale; confirm whether fractional quantities exist. |
| D10 | Who creates/edits items and Repair details; edit lock after release (U3). | Include in D6 map. |
| D11 | Document model: link tables only (X5); allowed formats, maximum size (DL unresolved) for request PDFs. | PDF only; set a size cap. |

### 5.3 Before Milestone 3

| # | Decision | Recommendation |
|---|---|---|
| D12 | Excel template (U32), conflict options (U33), all-or-nothing policy for errors (X29), unknown references (U4, U35), `ItemNo` rules (U36), import permission and duplicates inside a workbook (U37), omitted items (U38). | As recommended in those items. |

### 5.4 D13 — Before the stage/QC milestone (not blocking M1–M3)

X14 (QC-correction target), U14–U20 (quantity flow, scrap shortfall, rework, external semantics, route revision, unit capability, correction details), X15–X17 (status catalogue, external model, obsolete flags), U21–U25 (priority granularity, tie-break, override precedence, queue precedence, capacity), U27–U30 (downtime and calendar authority), U10–U12 (ContractRequired representation and contract numbering), U51–U55 (cost scope, contract-Make release, receipts), request-progress headline formula (DL unresolved; MS §12.5.1 vs MS update), U40 (Rial rounding), U39 (digits), U42 (reporting formats).

---

## 6. Implementation readiness

### 6.1 What genuinely blocks the initial solution and database model

| Blocks | Needs |
|---|---|
| Creating the solution (M1) | D1. D2 is strongly advised but does not technically block. |
| M1 database (Department / Role / User / audit) | D4; D6 for the minimum seed (an Admin role and its department). |
| M2 database (Customer, UOM, Request, workflow, RequestItem, RepairItemDetails, Document + links) | D5, D6, D7, D8, D9, D10, D11. |
| M3 (Excel) | D12. |

**Not blocking the initial model** (explicitly deferrable): scheduling, capacity, calendar entities, priority placement, ItemStage/QC/lot model, external dispatch, contracts, costing, warehouse, progress formula, Rial rounding, performance scoring, retention, SQL Server version, installer. M1–M3 must simply **avoid creating tables or columns for these** so that nothing has to be un-guessed later (for example omit `ContractRequired`, `Priority`, `PlannedStart/End` from M2).

### 6.2 Milestone 1 — Foundation

**Scope:** solution and projects per AS §3 (with UI/manual test plan); Persian RTL shell (login window, main window, chosen font); provider-neutral persistence base (EF Core candidate, SQLite, conventions from D4); `Department`, `Role`, `User`, audit table; authentication with salted password hashing; authorization service enforcing default-deny and encoding the 13 matrix rows as data; Jalali calendar abstraction and Rial money type; bidi spike for LTR identifiers in RTL; `.gitignore`; CI on a Windows runner.

**Acceptance criteria**
1. A clean checkout builds in Debug and Release on a Windows agent with no errors; Domain, Application and Infrastructure projects build and test on any OS.
2. All automated tests pass; the CI log is attached to the milestone report.
3. The app starts, shows the RTL Persian login, accepts a valid user, rejects wrong password and inactive user, and shows the main window.
4. No plaintext password anywhere: the stored value differs from the input, two users with the same password get different stored values, and no default password is committed.
5. Every row of DS §15.2 has an "allowed" and a "denied" test; an unlisted action is denied.
6. Jalali conversion passes a golden table (Nowruz boundaries, leap years, month ends); a Jalali date range converts to a half-open date range; no code path compares formatted strings.
7. Rial values format with thousand separators and a Rial label, parse back unchanged, and no Toman unit exists.
8. A database is created from migrations on SQLite; foreign keys enforced; concurrent-update conflict detected via the version column.
9. A grid shows `P-565`, `1.10`, `wsj-898` readable in RTL; a screenshot is attached.

**Tests:** unit (Domain, Application); integration against SQLite; manual UI checklist (RTL, Persian typing, window resize).

### 6.3 Milestone 2 — Customers, requests and items

**Scope:** Customer; UOM and conversions (QC-owned); WorkType; Request with the exact business key; `RequestWorkflowEntry` and the request state machine from creation through Management decision to the Planning/Drawing hand-off (per approved D5); mixed MAKE/REPAIR `RequestItem` and `RepairItemDetails`; document store with link tables and request PDF; request list/lookup with paging and an item grid for ~1,000 items.

**Acceptance criteria**
1. `(customer, "wsj-898", 1405)` and `(customer, "WSJ-898", 1405)` both save; an exact duplicate is rejected; the same number for another customer or year is accepted; edge-whitespace values are rejected per D7. Verified on SQLite and, before acceptance, on SQL Server.
2. Every transition in the approved table works; every other transition is rejected; each state change and its `RequestWorkflowEntry` commit or roll back together (fault-injection test).
3. Only Department = Management and Role = Manager can approve or reject initially; rejection requires a reason (MS §3.3); other departments are denied.
4. A request holds one MAKE and one REPAIR item (MS §39.1); two items with the same `PartName` remain distinguishable by `RequestNo + ItemNo` (MS §39.2).
5. `(RequestID, ItemNo)` is unique; `1.10`, `007`, `A-01` round-trip exactly.
6. UOM `PAIR→PCS = 2` yields `PCS→PAIR = 0.5` automatically; zero or negative is rejected; quantities keep their UOM.
7. One PDF linked to two records is stored once.
8. A request with 1,000 seeded items opens, scrolls and filters without freezing the UI; measured times are recorded and compared with the owner's target (U59).
9. Every state-changing action writes an audit record.

**Tests:** domain state-machine tests; application permission tests; integration tests on both providers; MS §39.1–39.2 as automated scenarios; manual UI checklist.

### 6.4 Milestone 3 — Excel import

**Scope:** official template (Persian headers, one mixed sheet) with a sample file containing no customer data; header-based parser; validator; preview; conflict detection and resolution; transactional commit; `ExcelImportBatch` and row-error audit; import summary; item counts by work type on the request page.

**Acceptance criteria** (cover MS §28.1 #1–13 and DS §21.3)
1. Import of 50 items succeeds; re-importing the same workbook plus 5 new rows adds exactly 5 and reports 50 as identical.
2. Changing quantity on one existing row shows a conflict with existing and incoming values side by side; commit is blocked until resolved; "keep existing" leaves the row and its history untouched.
3. Each error names the Excel row and reason; missing or invalid Work Type is an error and is never inferred; a numeric-typed `ItemNo` cell is flagged; duplicate `ItemNo` inside the workbook is flagged.
4. A failure during commit leaves zero new items and records the failed batch; with errors present, nothing is written (per D12).
5. Summary shows rows detected, accepted, identical, conflicted, rejected.
6. A 1,000-row import completes; the measured time is recorded; the UI remains responsive and the operation is cancellable.
7. Unauthorised users cannot import (D12/U37).
8. Request item totals refresh after import.

**Tests:** parser tests using workbooks generated in test code; integration on both providers; rollback test; performance test with recorded result; manual preview checklist.

### 6.5 What comes after M3 (not recommended for planning yet)

Stage definition and release, QC gate and quantity flow, external dispatch, contracts and gate, work sessions and queues, scheduling, costing, warehouse, dashboards. The stage/QC step should not start until D13 items U14, X14 and X15 are decided.

---

## Appendix A — Illustrative request-status draft for D5 (not approved)

Derived only from MS §3, §5.2, §6.1, and the non-obsolete entries of MS App. F.1. For discussion; the owner decides the real list.

| Draft code | Meaning | Source |
|---|---|---|
| `CREATED` | Central created the request | MS §3.2 |
| `SENT_TO_MANAGEMENT` | Awaiting initial Management decision | MS §3.2–3.3 |
| `MGMT_APPROVED_AT_PLANNING` | Approved, routed to Planning | MS §3.3 |
| `MGMT_REJECTED_AT_PLANNING` | Rejected with mandatory reason; Planning handles cancellation | MS §3.3 |
| `CANCELLED` / `CLOSED` | Terminal | MS App. F.1 |
| `UNDER_DRAWING_REVIEW` / `DRAWING_RETURNED_TO_PLANNING` | Drawing decision recorded (fully / partly accepted, rejected, need information) | MS §5.2 |

Open questions the table cannot answer: can a rejected request be reopened; can `CANCELLED` be reached from every state; how "need more information" loops; whether the later statuses (contract, production, completion) are derived or stored.

## Appendix B — Editorial errata and stale text (proposed amendments, none applied)

| File | Location | Issue |
|---|---|---|
| MS | §1.3 last paragraph; §13.1; §13.3; §14.6; §28 mapping table and App. C #10; §39.6 | Superseded by later decisions (X9, X19, X13, X28). |
| MS | Two sections numbered "1.4" (line ~236 and ~260) | Duplicate numbering. |
| MS | §12.5.1 | Says the item-completion metric is the default; the MS update says the formula is not yet approved. Reconcile wording (DL calls it "proposed"). |
| MS | §30.7 "described in Section 18" | Should be Section 19 (Costing). |
| MS | §33.13 "described in Section 27" | Should be Section 28. |
| MS | §33.1–33.3 | An orphan bullet ("shared production release") with no §33.2 heading; a section appears to be missing. |
| MS | §39 | Numbering jumps from 39.11 to 39.13. |
| MS | App. E.2, E.3 | Stray "- - -" bullet markers. |
| MS | "End of C#-Ready Master…" marker followed by Appendices E–G and the dated update | Document structure is misleading. |
| AS | §3 | Documentation list omits the Domain spec. |
| AS | §39.1 and §42 | Repeat the same decisions; keep one. |
| AS | §12.2 | Positional mapping table (see X28). |
| DS | §4.1 | "unique request number" (X6). |
| DS | §5.1, §5.3, §11.2 | Single document FKs vs link tables (X5). |
| DS | §32.2 vs §18 | Ordering wording (X20). |
| DS | §36 | Superseded checklist still present. |
| DL | lines 31 and 39 | Duplicate "progress percentage" entry. |
| DL | #19 | Wrong cross-reference (X36). |
| CM / RF | file paths | Do not match repository layout (X52). |

## Appendix C — Gaps in the Domain spec relative to the Master spec

Entities or fields required by MS or by DS's own text but not defined in the DS entity inventory:

`MachineDowntime` · working calendar / holiday / exception entities · shift pattern and break definitions · Friday/extra-hour approvals · `RouteRevision` · queue-override batch and detail · stage-unit capability · stage reassignment · contract decision history · cost/warehouse invalidation and reversal markers · per-session cost lines · `WarehouseInquiry` link to permission · user ↔ production-unit scope · `MakeItemDetails` (or an explicit decision to keep Make fields on `RequestItem`) · Customer field definition · generic audit-log table.

## Appendix D — Diff check

At the time of writing, the only intended change in the working tree is this file (`docs/PHASE0_SPECIFICATION_AUDIT.md`). No existing specification, log or instruction file was modified.
