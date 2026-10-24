# Claude Code Session Log

Append one dated entry after every substantial session. Do not replace prior entries.

## Session entry template

### Date / session
- Date:
- Claude Code session ID (if useful):

### Goal
- 

### Completed
- 

### Files changed
- 

### Decisions newly approved
- 

### Unresolved questions / risks
- 

### Commands/tests actually run and results
- 

### Recommended exact next action
- 

---

### Date / session
- Date: 2026-10-09
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.1)

### Goal
- Phase 0.1 repository housekeeping: correct the repository layout identified as finding X52 in `docs/PHASE0_SPECIFICATION_AUDIT.md`. Repository organisation only; no application work.

### Completed
- Created branch `claude/phase-0-1-repo-housekeeping` from `origin/main` (Phase 0 audit PR #1 was already merged).
- Moved with `git mv` (history preserved): `CLAUDE.md` to the repository root; the three specifications, `DECISION_LOG.md`, `SESSION_LOG.md` and `README_FIRST.md` into `docs/`. The now-empty `ERP_MES_Claude_Starter/` directory is gone.
- Added `.gitignore` (build output, local databases/backups, secrets, real business documents and exports) and `.claude/skills/.gitkeep`.
- Corrected one stale sentence in `docs/README_FIRST.md` (it told the reader to copy a package into the root). `CLAUDE.md` already used the `docs/...` paths and needed no edit.
- Added this entry.

### Files changed
- Moved: `ERP_MES_Claude_Starter/CLAUDE.md` -> `CLAUDE.md`; `ERP_MES_CSharp_Master_Specification_Current_State.md`, `ERP_MES_CSharp_Architecture_Specification.md`, `ERP_MES_CSharp_Domain_and_Database_Specification.md` -> `docs/`; `ERP_MES_Claude_Starter/docs/DECISION_LOG.md`, `ERP_MES_Claude_Starter/docs/SESSION_LOG.md`, `ERP_MES_Claude_Starter/README_FIRST.md` -> `docs/`.
- Created: `.gitignore`, `.claude/skills/.gitkeep`.
- Modified: `docs/README_FIRST.md` (one sentence), `docs/SESSION_LOG.md` (this entry).
- No business rule in any specification, `CLAUDE.md` or `DECISION_LOG.md` was changed.

### Decisions newly approved
- None. No item in the audit's decision register (D1-D13) was decided.

### Unresolved questions / risks
- No project skills exist anywhere in the repository. `README_FIRST.md` mentions `.claude/skills/`, but no skill files were ever supplied; none were created or installed. `.claude/skills/` holds only a `.gitkeep` placeholder.
- `.gitignore` excludes `*.pdf`, `*.xlsx`, `*.xls`, `*.xlsm`, `*.csv` repo-wide. A legitimate non-sensitive fixture (e.g. a sample import template) will need an explicit `!` exception when it is approved.
- `docs/PHASE0_SPECIFICATION_AUDIT.md` still describes the pre-move layout (X52) as a finding; it was intentionally not edited.
- All open items in the audit (D1-D13) remain open.

### Commands/tests actually run and results
- `git mv` for all moves; `rmdir` of the empty `ERP_MES_Claude_Starter/` tree.
- Byte comparison (`cmp`) against `origin/main`: `CLAUDE.md`, `docs/DECISION_LOG.md`, the three specifications and `docs/PHASE0_SPECIFICATION_AUDIT.md` are identical to their originals. `docs/README_FIRST.md` differs by one sentence. The original template text of `SESSION_LOG.md` is preserved as the prefix of the new file.
- `git ls-files -ci --exclude-standard`: no tracked file is matched by `.gitignore`. `git check-ignore` confirmed the sample patterns `*.db`, `bin/`, `*.pdf`, `.env` are ignored.
- `grep` for `ERP_MES_Claude_Starter`: remaining mentions are only in this log and in the audit report (historical description of X52).
- No build, test, or application code exists; none was run.

### Recommended exact next action
- Review and merge the housekeeping PR, then decide D1-D4 (see audit section 5.1) before Milestone 1.

---

### Date / session
- Date: 2026-10-09
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.1 amendment to PR #2)

### Goal
- Narrow amendment to PR #2: make the `.gitignore` rules for business documents less broad without weakening protection of private data.

### Completed
- Kept the repository-wide ignores for `*.pdf`, `*.xlsx`, `*.xls`, `*.xlsm`, `*.csv` as a safety net, and added explicit exceptions only for synthetic fixtures under `tests/fixtures/`.
- Added `tests/fixtures/README.md` documenting that directory: synthetic data only, never real or anonymised customer files, and no sub-directories named `data/` or `imports/` (ignored at any depth).
- Added `!.env.example` and `!.env.*.example` so placeholder templates are not ignored; real `.env` and `.env.*` remain ignored.
- Build output, local databases/backups, secrets and `customer-data/`, `real-data/`, `imports/` remain ignored.

### Files changed
- Modified: `.gitignore`, `docs/SESSION_LOG.md`.
- Created: `tests/fixtures/README.md`.
- No specification, decision log, `CLAUDE.md` or application code was touched.

### Decisions newly approved
- None. Choosing the "keep global rules plus fixtures exception" option was my implementation choice within the instruction; no audit decision (D1-D13) was made.

### Unresolved questions / risks
- `tests/fixtures/` currently contains only its README; no fixture files exist yet.
- `.gitignore` cannot prove a committed fixture is synthetic; that relies on review.

### Commands/tests actually run and results
- `git check-ignore -v` on probe paths. Ignored (rule shown): `customer.pdf`, `docs/customer.pdf`, `real-data/x.xlsx`, `.env`, `.env.local`, `app.db`, `src/bin/a.dll`, `obj/x`, `tests/fixtures/data/x.csv`. Allowed (negated rule shown): `tests/fixtures/sample_import.xlsx`, `tests/fixtures/sub_request.pdf`, `tests/fixtures/rows.csv`, `.env.example`. Not matched: `docs/DECISION_LOG.md`.
- `git status --short -uall` with the probe files present listed the three fixtures, `.env.example` and the README as trackable and none of the private examples.
- All probe files were then deleted; none were staged or committed.

### Recommended exact next action
- Review and merge PR #2, then decide D1-D4 (audit section 5.1) before Milestone 1.

---

### Date / session
- Date: 2026-10-09
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.2: D1, D3, D4)

### Goal
- Record the owner's answers to audit decisions D1, D3 and D4 (D2 was completed by Phase 0.1) and apply the D3 documentation amendments.

### Completed
- D1: recorded a provisional development target (latest .NET LTS at solution-creation time; `-windows` TFM for Desktop only) as Decision Log #22. No concrete version was named because it was not verified in this session.
- D4: owner reviewed each convention one by one and accepted all five: integer keys + app-managed version column (#23), UTC timestamps + date-only type (#24), provider conventions (#25), generic audit table (#26), PBKDF2 password hashing (#27).
- D3: owner chose "annotate + fix errata". Added `[Phase 0.1 annotation]` / `[Phase 0.1 editorial note]` blockquotes to stale passages (MS §1.3, §13.1, §13.3, §12.5.1, §14.6, §28, §39.6 inline, App. C #10; AS §7.2, §12.2; DS §5.1, §5.3, §11.2, §4.1, §32.2). Corrected: MS duplicate heading (second "1.4" is now "1.4A"), section cross-references (§30.7 -> 19, §33.13 -> 28), stray "- - -" list markers; AS docs tree now lists the Domain spec; DS §4.1 RequestNo wording; DL #19 cross-reference; DL duplicate "progress" entry merged. Recorded the amendment approach as Decision Log #28.
- Recording place: DECISION_LOG only (no ADR files), per owner's choice.

### Files changed
- `docs/DECISION_LOG.md` (new decisions #22-#28; #19 cross-reference; duplicate unresolved entry merged)
- `docs/ERP_MES_CSharp_Master_Specification_Current_State.md`, `docs/ERP_MES_CSharp_Architecture_Specification.md`, `docs/ERP_MES_CSharp_Domain_and_Database_Specification.md` (annotations and errata only)
- `docs/SESSION_LOG.md` (this entry)
- Not changed: `CLAUDE.md`, `docs/PHASE0_SPECIFICATION_AUDIT.md`, any code.

### Decisions newly approved
- D1 (provisional), D3, D4-a to D4-e as above. Decision Log #22-#28.

### Unresolved questions / risks
- Not annotated because they are still open decisions, not stale text: X14 (QC-correction target), X17 (NeedsIncomingQC / supply-type flags), X7 (Jalali year field; only noted in DS §4.1), X5/X10 (document links; only noted), all of D5-D13.
- Concrete .NET version is still to be verified and named in the Milestone 1 ADR; Windows 10 support-lifecycle claims in the audit remain "verify".
- The MS §33.2 and §39.12 gaps were only flagged; missing content was not reconstructed.
- The audit report still shows D1-D4 as open (historical); this log and the Decision Log are the current record.

### Commands/tests actually run and results
- Python string replacements asserted that each target passage occurred exactly once before editing (all succeeded).
- `git diff -U0` review: every removed line is one of the intended mechanical corrections listed above; all other changes are additions.
- `PHASE0_SPECIFICATION_AUDIT.md` and `CLAUDE.md` unchanged (empty diff).
- No build or tests exist; none run.

### Recommended exact next action
- Review and merge the Phase 0.2 PR, then decide D5-D11 (audit section 5.2) so the Milestone 2 schema can be designed; Milestone 1 can start once this PR is merged.

---

### Date / session
- Date: 2026-10-10
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (D1 amendment to PR #3)

### Goal
- Narrow update to D1: verify .NET 10 against the official Microsoft support policy and record it explicitly as the initial target.

### Completed
- Verified on 2026-10-10 from the official .NET support policy page: .NET 10 is LTS, original release 2025-11-11, end of support 2028-11-14, latest patch listed 10.0.12 (2026-09-08). The same page lists .NET 8 (LTS) and .NET 9 (STS) as ending support on 2026-11-10.
- Replaced Decision Log #22 (previously "latest LTS at solution creation, no version named") with an explicit target: .NET 10; `net10.0-windows` for the WPF Desktop project, `net10.0` for platform-neutral class libraries and test projects as applicable. Recorded as initial, not a production lock.
- Kept the minimum Windows edition/version and the production SQL Server version unresolved; reworded the "Exact .NET target/runtime and SQL Server..." unresolved bullet to cover only the SQL Server version/provider and to point to #22.
- Checked Microsoft's .NET 10 supported-OS page: its Windows client row lists Windows 11 builds and only certain Windows 10 entries (21H2 marked "(E)" and "(IoT)", 1809 "(E)", 1607 "(E)"). The page did not expand "(E)"/"(IoT)" in the text retrieved, so this log does not interpret them; the Windows 10 baseline stays open and no blanket Windows 10 support is claimed.

### Files changed
- `docs/DECISION_LOG.md` (#22 rewritten; one unresolved bullet reworded)
- `docs/SESSION_LOG.md` (this entry)
- Not changed: D4 conventions (#23-#27), any other decision, specifications, `CLAUDE.md`, audit report.

### Decisions newly approved
- D1 amended by the owner: .NET 10 as the initial target with the target-framework split above. No new business decision.

### Unresolved questions / risks
- Minimum Windows edition/version, and production SQL Server version/provider: unresolved until the deployment environment is checked.
- The factory's Windows 10 machines (if any are kept) must be matched against the supported-OS list; the (E)/(IoT) qualifiers need to be read from Microsoft's page directly before deciding.
- `CLAUDE.md` still says "Exact .NET version and Windows 10 baseline must be verified before framework lock-in". That remains accurate for production and was not edited.
- The earlier Phase 0.2 entry above (written 2026-10-09) says no concrete version was named; that was true then and is superseded by #22 as amended.

### Commands/tests actually run and results
- Fetched the official .NET support policy page and the .NET 10 supported-OS page; values quoted above are from those fetches (the page content was summarised by the fetch tool, not read as raw HTML).
- `git diff` reviewed: only the intended lines in the two files changed.
- No build or tests exist; none run.

### Recommended exact next action
- Review and merge PR #3, then decide D5-D11 (audit section 5.2). Before Milestone 1 acceptance, read the "(E)"/"(IoT)" notes on Microsoft's supported-OS page and inventory the factory machines.

---

### Date / session
- Date: 2026-10-10
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.3: D5-D11)

### Goal
- Record the owner's answers to audit decisions D5-D11 so the Milestone 2 schema can be designed.

### Completed
- Asked each decision with the audit's recommendation first, and recorded the answers as Decision Log #29-#35: D5 request statuses (rejected requests can be reopened by Planning or Management), D6 departments/roles seed, D7 RequestNo key details, D8 Customer (name only), D9 decimal fixed-scale quantities, D10 provisional permission map, D11 documents (link tables, PDF only, 10 MB).
- Updated the Unresolved section: authorization-matrix and document bullets annotated; added two new open items (request-status transition table; role requirement for reopening).

### Files changed
- `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md` only.
- Not changed: specifications, `CLAUDE.md`, audit report, any code.

### Decisions newly approved
- D5-D11 as Decision Log #29-#35. Where an answer did not cover a detail (for example whether CANCELLED/CLOSED are terminal, content-hash de-duplication, the decimal scale), the detail was recorded as undecided or as a proposal, not as approved.

### Unresolved questions / risks
- The specifications still contain the older wording (for example `[DECISION REQUIRED]` markers in the Domain spec, the `Customer`/`JalaliRequestYear` fields not yet in DS §4.1). CLAUDE.md asks that specifications be updated when a decision is approved; that sync was not done in this change and should be a follow-up so the Domain spec matches #29-#35.
- Request-status transition table still has to be written and reviewed before the Milestone 2 state machine.
- D13 items (QC-correction target, quantity flow, contracts, scheduling, costing, progress formula) remain open and do not block Milestones 1-3.
- Windows baseline and SQL Server version remain unresolved (see #22 and Unresolved).

### Commands/tests actually run and results
- `git diff` reviewed: 11 lines added and 2 lines edited in the Decision Log; the two edits only append clarifying text to existing Unresolved bullets.
- No build or tests exist; none run.

### Recommended exact next action
- Review and merge this PR. Then (1) sync the Domain/Database specification with #29-#35 and draft the request-status transition table for review, and (2) start Milestone 1 (D1-D4 are decided) with a solution/projects plan for approval before any code is written.

---

### Date / session
- Date: 2026-10-10
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.4: PR #4 merge, Domain sync, transition draft, Milestone 1 plan)

### Goal
- Merge PR #4 (owner request), synchronise the Domain and Database Specification with Decision Log #29-#35, draft the request-status transition table, and prepare the Milestone 1 solution and project plan for approval. No code.

### Completed
- PR #4 was a draft; marked it ready for review at the owner's request and merged it (merge commit a675df2).
- Domain spec: added `JalaliRequestYear` to Request fields (§4.1); RequestNo rules (§4.2); status set and reopen rule (§4.3); Customer entity (§4.5); document references now link-table associations (§5.1, §5.3, §11.2); quantity precision note (§5.1); roles/departments seed (§15.3); provisional permission map (§15.4); reopen row in the matrix (§15.2, §32.8); document rules (§20); new §38 summarising the decisions. Earlier "Phase 0.1 editorial note" blocks about the document FKs and the missing year field were replaced because the decisions now exist.
- New draft `docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md`: 11 transitions, each marked as from-spec/decision or Proposed, with open questions and the tests it implies.
- New `docs/MILESTONE_1_PLAN.md`: scope, solution layout, components, package candidates (none installed), work breakdown M1.1-M1.7, acceptance criteria, environment limits, risks, and ten approval items (A1-A10).

### Files changed
- Modified: `docs/ERP_MES_CSharp_Domain_and_Database_Specification.md`, `docs/SESSION_LOG.md`.
- Created: `docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md`, `docs/MILESTONE_1_PLAN.md`.
- Not changed: Master and Architecture specifications, Decision Log, `CLAUDE.md`, audit report, any code.

### Decisions newly approved
- None new. The transition table and Milestone 1 plan are proposals awaiting approval.

### Unresolved questions / risks
- Transition-table questions (terminal statuses, role for reopen, item-edit statuses, extra cancellations): see the draft, section 4.
- Milestone 1 approvals A1-A10 (layout, packages, Jalali approach, auth policy, font, CI, SDK install).
- The Architecture Specification is not updated to mention the extra `Infrastructure.Sqlite` project; that edit waits for approval of the plan.
- Master Specification wording not synced with #29-#35 beyond earlier annotations.

### Commands/tests actually run and results
- Python replacements asserted a single match before editing; `git diff` reviewed: removed lines are the replaced table rows and editorial notes only.
- Rendering check of the Domain spec edit: found and fixed a paragraph directly above `---` that markdown would render as a heading.
- No build or tests exist; none run. No .NET SDK is installed in this environment.

### Recommended exact next action
- Review the transition draft and the Milestone 1 plan, answer the open questions and approvals, then I record them in the Decision Log and start M1.1 only.

---

### Date / session
- Date: 2026-10-11
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.5: Milestone 1 plan revision 2, PR #5 still draft)

### Goal
- Revise the Milestone 1 plan and affected documents before approval: narrow Milestone 1 to the technical foundation, document project responsibilities, define a secure one-time Administrator bootstrap, and set technical defaults. No code.

### Completed
- Architecture Specification: §38 rewritten into Milestones 1-3 (the earlier first-milestone list that included Request/RequestItem, workflow engine, Excel import and MAKE/REPAIR is superseded); §3 solution tree and §3.4 updated with `Infrastructure.Sqlite` and a future `Infrastructure.SqlServer`, marked "Proposed - pending approval".
- `docs/MILESTONE_1_PLAN.md` rewritten (revision 2): scope and explicit exclusions; project structure, dependency direction and per-project responsibilities; provider-independence explanation; `JalaliDateService` on `PersianCalendar`; bootstrap mechanism (one-time token, `SystemSetup` state machine, atomic conditional claim, concurrent initialization, residual risks, tests); configurable authentication defaults (12 characters, 5 attempts, 5 minutes); PBKDF2-HMAC-SHA256 at 600,000 with stored algorithm/version/work factor and a benchmark on the lowest-spec PC; minimal package list with justifications and exclusions; central package management; SDK pin proposal; font chain; Windows CI; work breakdown, acceptance criteria, risks; 15 proposed decisions (P1-P15).
- Transition draft and Domain spec §4.3 now state that the status-transition table is not approved, must not be implemented, and its review is deferred until before the request-workflow milestone; its open questions are unchanged and visible.

### Files changed
- Modified: `docs/ERP_MES_CSharp_Architecture_Specification.md`, `docs/ERP_MES_CSharp_Domain_and_Database_Specification.md` (one sentence), `docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md` (status header), `docs/MILESTONE_1_PLAN.md`, `docs/SESSION_LOG.md`.
- Not changed: Master Specification, Decision Log, `CLAUDE.md`, audit report, any code, `.gitignore`.

### Decisions newly approved
- None. All items in the plan are proposals (P1-P15). The Decision Log was not changed.

### Unresolved questions / risks
- Verified from the web in this session: .NET 10 SDK 10.0.401 / runtime 10.0.12 (Microsoft .NET 10 download page; only the first part of the long page was read) and OWASP's 600,000 iterations for PBKDF2-HMAC-SHA256. Two other hosts (builds.dotnet.microsoft.com, cheatsheetseries.owasp.org) were unreachable from this environment; equivalent pages on dotnet.microsoft.com and github.com were used instead.
- Not verified in this session: NuGet licences/compatibility of each package (listed as "expected; verify"), availability of fonts on factory machines, hash timing on any PC.
- Break-glass recovery for a lost Administrator is not designed (P12).
- Production SQL Server version/provider, minimum Windows edition/version, Rial rounding, request-status transitions remain unresolved.

### Commands/tests actually run and results
- Web fetches listed above. `git diff` reviewed (see PR). Searched the documents for stale terms (`IJalaliCalendar`, old approval numbering). No build or tests exist; no SDK was installed; none run.

### Recommended exact next action
- Review `docs/MILESTONE_1_PLAN.md` §13 (P1-P15) and reply with approvals or changes; then I record the approved items in the Decision Log and start step M1.1 only.

---

### Date / session
- Date: 2026-10-11
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (Phase 0.6: P1-P15 approvals recorded)

### Goal
- Record the owner's P1-P15 decisions, update the Milestone 1 plan and Architecture documentation, then authorize M1.1 only.

### Completed
- Decision Log #36-#50 (P1-P15) added; two Unresolved bullets added (Rial rounding/storage; central authentication-policy storage).
- `docs/MILESTONE_1_PLAN.md` revised to revision 3: approval status; Provisioning without embedded credentials; token safeguards (digest only, re-issue invalidates, central attempt limit); new section 5.7 Administrator recovery (last-Administrator protection with a concurrency stamp, restricted Provisioning recovery token, forced new password, no backdoor, provider-neutral SQL Server process); real package table (versions, licence and target frameworks read from NuGet metadata on 2026-10-11) marked **not yet approved**; Tahoma-first font chain; CI action SHAs resolved; SDK pin text; section 13 turned into an approval-status table.
- Architecture Specification: "pending approval" notes changed to "approved" with Decision Log references; Provisioning project added to the tree and to Milestone 1 item 6.

### Files changed
- `docs/DECISION_LOG.md`, `docs/MILESTONE_1_PLAN.md`, `docs/ERP_MES_CSharp_Architecture_Specification.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- P1-P15 (Decision Log #36-#50). P8 is conditional: the package table awaits approval.

### Unresolved questions / risks
- P8 package table (including xUnit v2 versus v3) and the `rollForward` setting await the owner's answer.
- NuGet vulnerability advisories for the listed versions not yet checked; first CI restore will report.
- Production SQL Server version/provider, minimum Windows edition/version, Rial rounding, central authentication-policy storage and all request-status transition questions remain unresolved.

### Commands/tests actually run and results
- NuGet metadata reads (api.nuget.org) and `git ls-remote` for action tag SHAs; `git diff` reviewed. No build or tests; no SDK installed.

### Recommended exact next action
- Review M1.1 (next pull request) and answer the P8 table questions before M1.2.

---

### Date / session
- Date: 2026-10-11
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (M1.1: solution skeleton)

### Goal
- Implement Milestone 1 step M1.1 only, according to the approved plan.

### Completed
- Added `ERP-MES.sln` (9 projects in `src` and `tests` solution folders), `global.json` (SDK 10.0.401, exact pin), `Directory.Build.props`, `Directory.Packages.props` (central package management on, no package versions), `.editorconfig`, six `src` projects and three `tests` projects with the approved project references and **no package references**, and `.github/workflows/ci.yml`.
- `Desktop` (`net10.0-windows`, WPF) and `Provisioning` contain only a placeholder entry point; the libraries and test projects contain no source files.
- CI: Windows runner; actions pinned to full SHAs (checkout v7.0.1, setup-dotnet v6.0.0, upload-artifact v7.0.2); `contents: read` permissions; no secrets or deployment; verifies the SDK in use equals the `global.json` pin; restore, Debug and Release builds, test with TRX upload.
- Plan section 9 updated to the exact pin implemented.

### Files changed
- Created: `ERP-MES.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.github/workflows/ci.yml`, nine `.csproj` files, `src/ERP.MES.Desktop/Program.cs`, `src/ERP.MES.Provisioning/Program.cs`.
- Modified: `docs/MILESTONE_1_PLAN.md` (section 9, section 13 wording), `docs/SESSION_LOG.md`.

### Decisions newly approved
- None. Implementation choices made inside the approved plan, reported for review: exact SDK pin (`rollForward: disable`); hand-written classic `.sln`; placeholder entry points; no `TreatWarningsAsErrors` (not approved).

### Unresolved questions / risks
- **Not built or tested locally:** no .NET SDK is installed in the Claude environment and none was installed. Build and test results come from GitHub Actions only (see below).
- **CI result for commit 7dc9419 (PR #6, run 38174776676, job 114574753659):** success. Windows Server 2025 runner; SDK 10.0.401 selected and equal to the `global.json` pin; restore OK; Debug build and Release build of all 9 projects (including the WPF `net10.0-windows` project): 0 warnings, 0 errors; `dotnet test` finished with no tests discovered or run (zero test projects with a test framework; the step succeeded); the artifact upload step found no TRX files and uploaded nothing, as expected; the three actions ran at the pinned SHAs; token permissions were `Contents: read` and `Metadata: read`. Not yet observed: NuGet audit output (no packages) and any test execution.
- Package table (P8) and xUnit v2/v3 choice await approval; test projects are placeholders until then.
- Production SQL Server version/provider, minimum Windows edition/version, Rial rounding, central authentication-policy storage, request-status transitions remain unresolved.

### Commands/tests actually run and results
- GitHub Actions CI on PR #6: see the CI result above.
- A Python verification script (kept outside the repository) checked: JSON/YAML/XML well-formedness; workflow permissions and SHA pinning, no secrets; exactly nine projects whose references equal the approved graph, no cycles, no package references, correct target frameworks, only Desktop uses WPF; the solution file lists exactly those projects with consistent GUIDs, configurations and nesting, CRLF endings; central package management has zero package versions; only two source files exist. Result: PASS.
- `git check-ignore` / `git status` confirmed no new file is hidden by `.gitignore`.
- No build or test was run locally.

### Recommended exact next action
- Review the M1.1 pull request and its CI run. Then answer the P8 package-table questions so M1.2 (`JalaliDateService`, `Money`) can start.

---

### Date / session
- Date: 2026-10-11
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (M1.1 approval confirmations, PR sequence)

### Goal
- Act on the M1.1 approval: merge PR #5, retarget PR #6, introduce xUnit v3 with real tests, run restore and vulnerability checks, re-run CI on the retargeted branch. M1.2 not started.

### Completed
- Reviewed PR #5's complete diff before merging: 6 files, all under `docs/`; Master spec, `CLAUDE.md`, audit report untouched; Decision Log changes purely additive. Merged PR #5 (merge commit `b52def0`), pinned to the reviewed head SHA.
- Retargeted PR #6 to `main`; merged `main` into the branch so it is current (`70dcdc5`).
- Decision Log #51 (exact SDK pin; must be installed locally and in CI), #52 (xUnit v3), #53 (package acceptance gate).
- xUnit v3 set up as in the official `xunit.v3` template 4.0.2, VSTest variant: shared settings in `tests/Directory.Build.props`, exact versions in `Directory.Packages.props`, `tests/xunit.runner.json`, three test projects kept. 11 real tests guard the approved project dependency direction (Domain 3, Application 3, Infrastructure 5); they read the project files, not business code.
- CI additions: dependency listing, transitive vulnerability gate, a check that every test project discovered and passed tests (zero tests fails), per-project TRX files.
- Plan sections 8, 8.1, 9, 11, 12, 13 updated; section 8.1 holds the batch 1 results.

### Files changed
- Created: `tests/Directory.Build.props`, `tests/xunit.runner.json`, `tests/Shared/SolutionLayout.cs`, three `*DependencyTests.cs` files.
- Modified: `Directory.Packages.props`, `.github/workflows/ci.yml`, three test `.csproj` files, `docs/DECISION_LOG.md`, `docs/MILESTONE_1_PLAN.md`, `docs/SESSION_LOG.md`.
- Not changed: request/item schema, workflows, Excel, production code (none exists), specifications other than the Decision Log and plan.

### Decisions newly approved
- None beyond the owner's confirmations recorded as #51-#53.

### Unresolved questions / risks
- Batch 1 awaits the owner's acceptance. Remaining package rows in plan section 8 stay conditional and add nothing.
- The Microsoft.Testing.Platform runner was not adopted (Microsoft's documentation retrieved here did not state the global.json opt-in JSON and needs an extra TRX package); a later switch would be a separate reviewed change.
- Licences of transitive packages were not reviewed individually beyond package metadata.
- Production SQL Server version/provider, minimum Windows edition/version, Rial rounding, central authentication-policy storage and all request-status transition questions remain unresolved and unchanged.

### Commands/tests actually run and results
- Authoritative results are from GitHub Actions run 38175638879 (commit `cbb36da`, PR #6 with base `main`): SDK 10.0.401 selected and verified; restore succeeded; transitive vulnerability check: no vulnerable packages in any of the 9 projects; Debug and Release builds 0 warnings, 0 errors; tests 11 discovered, 11 passed, 0 failed; three TRX files uploaded.
- Local, outside the build (no SDK installed): a Python structure check (XML, YAML and JSON validity, project graph, solution file, CI hygiene, test inventory) which also caught an illegal double hyphen inside an XML comment before it was pushed; a read of the official xUnit v3 template package; NuGet metadata and advisory-feed reads. A follow-up run for the documentation commit that adds this entry is reported in the pull request.

### Recommended exact next action
- Review PR #6 (CI is green on the retargeted branch), accept or amend batch 1 in plan section 8.1, then decide whether to start M1.2 (`JalaliDateService`, `Money`).

---

### Date / session
- Date: 2026-10-11
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (final M1.1 review: licence inventory)

### Goal
- Before merging PR #6: add a licence inventory for all direct and transitive packages used by the test projects, flag missing or unclear licences without guessing, recheck the PR scope, report CI status. M1.2A not started.

### Completed
- Added `docs/DEPENDENCY_LICENCE_INVENTORY.md`: 16 packages (3 direct, 13 transitive) at the exact resolved versions. Licences read from each package's `.nuspec` SPDX expression: 7 MIT, 9 Apache-2.0; none missing, unclear, copyleft or proprietary. Flags: four Microsoft packages declare `requireLicenseAcceptance=true`; five packages bundle third-party notices (Mono.Cecil MIT, Newtonsoft.Json MIT, NuGet.Client Apache-2.0, and the standard .NET runtime notices). All packages are used by test projects only.
- The inventory table was cross-checked against the machine-collected manifest data (16 of 16 rows match). No package version was changed.
- Plan section 8.1 now points to the inventory.

### Files changed
- Created: `docs/DEPENDENCY_LICENCE_INVENTORY.md`. Modified: `docs/MILESTONE_1_PLAN.md` (one bullet), `docs/SESSION_LOG.md`.

### Decisions newly approved
- None.

### Unresolved questions / risks
- Owner review of the inventory and acceptance of the MIT terms flagged in F1.
- Limits stated in the inventory: declared licences taken as stated; the long .NET third-party notice file was read by headings and licence families only; no legal review; repository-signature presence checked, not validated.
- Production SQL Server version/provider, minimum Windows edition/version, Rial rounding, central authentication-policy storage and request-status transitions remain unresolved and unchanged.

### Commands/tests actually run and results
- Downloaded and read the 16 `.nupkg` archives from api.nuget.org into an isolated scratch directory (nothing executed); read the bundled notice files for the five packages that have them; verified the markdown table against the collected data (16/16); recounted licences (7 MIT, 9 Apache-2.0).
- CI result for the commit that adds this entry is reported in PR #6.

### Recommended exact next action
- Review the inventory, then merge PR #6; M1.2A (`JalaliDateService` only) starts after that on your instruction.

---

### Date / session
- Date: 2026-10-11
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (M1.2A: JalaliDateService)

### Goal
- Implement `JalaliDateService` only (Decision Log #45) on a new branch from `main` after PR #6 was merged. No Money/Rial, no database, no request/item, Excel, workflow or production functionality.

### Completed
- `src/ERP.MES.Domain/Calendar/`: `IJalaliDateService`, `JalaliDateService` (built on `System.Globalization.PersianCalendar`, no package), `JalaliDate`, `DateRange`, `DigitStyle`, `JalaliDateError`. Conversion, year, leap-year and month-length queries, year/month/inclusive ranges as half-open Gregorian `DateRange`, formatting (`yyyy/MM/dd`, `yyyy/MM/dd HH:mm` 24-hour) and strict parsing (Latin and Persian digits), all in this one service. Storage conventions of Decision Log #24 are followed: `DateOnly` for dates, UTC `DateTime` for timestamps, no `DateTimeOffset`.
- Invalid input returns a specific `JalaliDateError` and a default value; nothing is silently corrected. Digit shape and display time zone are required constructor arguments because neither is decided.
- Tests in `tests/ERP.MES.Domain.Tests/Calendar/`: golden pairs in both directions, Nowruz and year boundaries, leap and common years, round trips for every day 1900-2100, month/year range tiling, output formats, Persian-digit and mixed-digit input, invalid-input tables, daylight-saving gap/overlap using a test-only time zone.
- `docs/JALALI_DATE_SERVICE.md` documents the behaviour, error order, golden data for review and open questions Q1-Q6. Decision Log Unresolved list gained one bullet for those questions. Plan section 3.1/12/status updated; M1.2 split into M1.2A (in review) and M1.2B (not started).
- Licence inventory retained; its status line and plan section 8.1 now state it is an engineering record and not a legal approval of the package terms.

### Files changed
- Created: six files in `src/ERP.MES.Domain/Calendar/`; six files in `tests/ERP.MES.Domain.Tests/Calendar/`; `docs/JALALI_DATE_SERVICE.md`.
- Modified: `docs/DECISION_LOG.md` (one Unresolved bullet), `docs/MILESTONE_1_PLAN.md`, `docs/DEPENDENCY_LICENCE_INVENTORY.md` (status line), `docs/SESSION_LOG.md`.
- Not changed: package versions, CI workflow, other projects.

### Decisions newly approved
- None. Presentation choices are documented as open questions, not decided.

### Unresolved questions / risks
- Q1-Q6 in `docs/JALALI_DATE_SERVICE.md` section 7 (digit shape on screen, display time zone, input leniency, display format, daylight-saving edge cases, acceptance of the golden-date source).
- The golden dates are from general knowledge cross-checked for mutual consistency; they are not from a published table. Future-year rows (1406-1409) are least certain.
- Rial rounding and storage, SQL Server version/provider, minimum Windows edition/version, authentication-policy storage and request-status transitions remain unresolved and unchanged.

### Commands/tests actually run and results
- Not compiled or run locally (no SDK installed in this environment). Local checks only: a Python structure check (XML, project graph, expected files, no out-of-scope terms, ASCII-only service source), and a line-for-line Python port of the parsing/validation logic run against every row of the invalid-input and valid-input test tables (0 mismatches over 96 rows). These check my expectations, not the compiled code.
- A source-encoding slip was found and fixed before pushing: Persian digit characters had been written literally in the service; they are now `\u06F0`-style escapes.
- **First CI run of the PR (run 38177552338, commit `9d85899`): failed at the test step.** Restore, vulnerability check, Debug build and Release build succeeded with 0 warnings and 0 errors, so the code compiles. Tests: Infrastructure 5/5 and Application 3/3 passed; Domain 206 passed, **1 failed** of 207. The failing test, `The_day_after_the_last_supported_day_is_rejected`, asserted a precondition I had assumed without verifying (that the calendar's last supported day is not the last day of its month); `PersianCalendar` reports that final month as ending at the last supported day. The service itself was consistent (the day after is rejected). All golden-date, leap-year, boundary, Persian-digit and other invalid-input tests passed.
- Fix (next commit): the test was rewritten to assert only what must hold in either case (the next day, the next month and the next year are rejected with a specific error and no value), and `docs/JALALI_DATE_SERVICE.md` section 4 now states that a day after the last supported day inside its own month is `DayOutOfRange`. - **Second CI run (run 38177754591, commit `37a78f8`): failed again, same test, 206 of 207.** It revealed a genuine service bug: `PersianCalendar.GetDaysInMonth` throws `ArgumentOutOfRangeException` for months after the last supported month of the last supported year, so `TryCreateDate` and parsing threw instead of returning an error. Fix: `IsAfterLastSupportedMonth` guard; `TryCreateDate`/parse now return `YearOutOfRange` for those months, `GetDaysInMonth` throws `ArgumentOutOfRangeException`; the test now covers all later months; `docs/JALALI_DATE_SERVICE.md` section 4 updated. The CI result of the fix commit is reported in the pull request.

### Recommended exact next action
- Review the pull request and its CI run, answer Q1-Q6 as you wish, and decide when M1.2B (`Money` formatting and result type; Rial rounding stays out unless you decide it) may start.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (PDF size limit, documentation only)

### Goal
- Change the maximum request-PDF size from 10 MB to 20 MiB everywhere it is stated. Documentation only; no upload code.

### Completed
- PR #8 (a revert of PR #7) was closed without merging at the owner's instruction; the JalaliDateService on `main` is kept.
- Decision Log #54 records 20 MiB (20,971,520 bytes), displayed as "20 MB", configurable, PDF only; #35 and the Unresolved bullet point to it.
- Domain and Database Specification (Documents rule and the summary list) updated.

### Files changed
- `docs/DECISION_LOG.md`, `docs/ERP_MES_CSharp_Domain_and_Database_Specification.md`, `docs/SESSION_LOG.md`.
- Searched all of `docs/`, `CLAUDE.md` and the repository for other size references: the Master and Architecture specifications state no PDF size, so they are unchanged. The historical audit (D11) and earlier session-log entries still say 10 MB because they record what was true then.

### Decisions newly approved
- #54 (above).

### Unresolved questions / risks
- Nothing new. When upload is implemented, tests must cover exactly 20,971,520 bytes (accepted) and 20,971,521 bytes (rejected).

### Commands/tests actually run and results
- Documentation only; no build or test run locally. Windows CI result is reported in the PR.

### Recommended exact next action
- Review and merge this PR, then the Q1-Q6 JalaliDateService PR is started from the updated `main`.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (CI artifact for the demonstration build)

### Goal
- D1.4 of the demo plan: let the owner download and run the CI-built application.

### Completed
- `.github/workflows/ci.yml`: after all checks and tests pass, and **only on a push to `main`**, a self-contained win-x64 publish of the Desktop project, a `BUILD-INFO.txt` with the commit SHA and run id, and an upload as artifact `demo-build-win-x64-<sha>` (14 days). Pull-request runs do not publish (owner condition, 2026-10-14). Same pinned `upload-artifact` action, same permissions. `docs/MILESTONE_1_PLAN.md` section 11 records the narrow exception and the runtime-pack limitation.

### Files changed
- `.github/workflows/ci.yml`, `docs/MILESTONE_1_PLAN.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- The owner approved the goal and conditions for this narrow exception to the "no publishing step" rule on 2026-10-14; the PR is for the owner's review and merge.

### Unresolved questions / risks
- The RID-specific restore fetches runtime packs that the vulnerability gate does not cover. The published folder is large (self-contained WPF).

### Commands/tests actually run and results
- Not run locally. On pull-request runs the new steps are skipped by design, so the publish steps themselves are first exercised by the push to `main` after the merge; an earlier pull-request run of the first version of this change did publish successfully (run 38640168245, artifact about 65 MB).

### Recommended exact next action
- Review and, if you approve the amendment, merge; then download the artifact from the run and work through `docs/MANUAL_UI_CHECKLIST.md`.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (package batch 3: EF Core Design and dotnet-ef)

### Goal
- Add the approved migration tooling (Design 10.0.13 private, `dotnet-ef` 10.0.13 pinned), with restore, vulnerability and licence checks. No DbContext, migration, schema or `EnsureCreated`.

### Completed
- `Microsoft.EntityFrameworkCore.Design` in `ERP.MES.Infrastructure.Sqlite` and `ERP.MES.Provisioning` (`PrivateAssets="all"`), `.config/dotnet-tools.json`, CI tool restore and version check, three tests, plan section 8.3, licence inventory section 7, Decision Log #56.

### Files changed
- `Directory.Packages.props`, the two project files, `.config/dotnet-tools.json`, `.github/workflows/ci.yml`, `tests/ERP.MES.Infrastructure.Tests/EfToolingTests.cs`, `docs/MILESTONE_1_PLAN.md`, `docs/DEPENDENCY_LICENCE_INVENTORY.md`, `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- #56.

### Unresolved questions / risks
- Notice files and the embedded SQLite binary are not examined (inventory sections 6 and 7); to be investigated before broader deployment or redistribution. The vulnerability check does not cover the `dotnet-ef` tool. Whether the migrations project can serve as its own startup project is not needed: Provisioning is the executable startup project.

### Commands/tests actually run and results
- Not built locally (no SDK). CI run 38647090975 on `e624d39`: restore, tool restore, `dotnet ef --version` = 10.0.13, no vulnerable packages, 0 warnings / 0 errors, 233 of 233 tests. The documentation commit has its own CI run, reported in the PR.

### Recommended exact next action
- Review and merge; then the migration PR (DbContext for the approved subset, generated initial migration and snapshot).

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (four-day demo plan, documentation only)

### Goal
- Write `docs/FOUR_DAY_DEMO_PLAN.md`: a realistic plan for a working demonstration within four days. No code.

### Completed
- Plan with scope, exclusions, four-day PR schedule, must/should/cut list, temporary demonstration assumptions (T1-T6), package and CI notes, decisions requested, meeting walkthrough and risks.

### Files changed
- Created `docs/FOUR_DAY_DEMO_PLAN.md`; modified `docs/SESSION_LOG.md`.

### Decisions newly approved
- None. Every assumption in the plan is a proposal awaiting the owner.

### Unresolved questions / risks
- Owner decisions listed in plan section 7 (assumptions, sign-in approach, EF Core/SQLite approval, ordering against the date follow-up).
- I cannot run WPF here, so screen quality depends on the owner running CI-built artifacts.
- Status transitions, permissions matrix, scheduling details, Rial rounding, SQL Server version, Windows baseline remain unresolved and unchanged.

### Commands/tests actually run and results
- Documentation only; nothing built or run locally. Windows CI result is reported in the PR.

### Recommended exact next action
- Review the plan and answer section 7; merge in order: PDF-size PR, then this PR (resolve the SESSION_LOG overlap by merging `main` first), then the Q1-Q6 follow-up starts.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (demo plan updated from `main`)

### Goal
- Bring PR #10 up to date with `main` after the PDF-size PR merged, record the owner's demo decisions and the migrations-only rule.

### Completed
- Merged `main` into the plan branch (one `SESSION_LOG.md` conflict resolved by keeping both entries). Plan: dependency table, D1.3 split into tooling and persistence PRs, demo acceptance criteria (section 4.4), protected credential file. Decision Log #57 records T1-T6 and the migrations rule.

### Files changed
- `docs/FOUR_DAY_DEMO_PLAN.md`, `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- #57 (owner decisions of 2026-10-14). Numbers #55 and #56 belong to the package PR and the migration-tooling PR.

### Unresolved questions / risks
- Decision Log numbering has a temporary gap on `main` until PR #11 and the tooling PR merge. Request-status transitions, full permissions, scheduling, Rial rounding, SQL Server version and the Windows baseline stay unresolved.

### Commands/tests actually run and results
- Documentation only; Windows CI result is reported in the PR.

### Recommended exact next action
- Review and merge PR #11, then #10 and #12 in the order you prefer; the tooling PR then starts.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (demo plan refreshed after #11-#13 merged)

### Goal
- Bring PR #10 up to date with `main` after the packages, shell and artifact PRs merged.

### Completed
- Merged `main`; resolved `DECISION_LOG.md` (#55 from main, #57 kept) and `SESSION_LOG.md` by keeping main's entries and re-appending this branch's. Plan status lines updated: packages, shell and artifact merged; migration tooling in PR #15; domain model in PR #14.

### Files changed
- `docs/FOUR_DAY_DEMO_PLAN.md`, `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- None.

### Unresolved questions / risks
- #57 stays unrecorded in sequence until #56 (PR #15) and #58 (PR #14) are merged; merge #10 after #56 is recorded.

### Commands/tests actually run and results
- Documentation only; Windows CI result is reported in the PR.

### Recommended exact next action
- Merge PR #15 (records #56), then this PR.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (demo plan refreshed after #15 merged)

### Goal
- Bring PR #10 up to date with `main` after the migration-tooling PR (#15) merged.

### Completed
- Merged `main`; `DECISION_LOG.md` is sequential (#54, #55, #56 from main, then #57 kept) and `SESSION_LOG.md` keeps main's entries plus this branch's. Plan status lines updated (tooling merged; migration PR next).

### Files changed
- `docs/FOUR_DAY_DEMO_PLAN.md`, `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- None.

### Unresolved questions / risks
- None new. #58 is in PR #14 and follows #57 once both are merged.

### Commands/tests actually run and results
- Documentation only; Windows CI result is reported in the PR.

### Recommended exact next action
- Review and merge this PR; then PR #14 is refreshed from `main`.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (domain model for the demonstration)

### Goal
- Model Customer, Request, mixed MAKE/REPAIR items, seeded stages and priority in the Domain project, strictly from the Domain and Database Specification and the Decision Log. No persistence, EF Core or migrations.

### Completed
- `src/ERP.MES.Domain/Requests/`: `Customer`, `RequestNo`, `RequestKey`, `Request`, `RequestStatus`, `RequestItem`, `WorkType`, `Priority`, `ItemStage`, `ItemStageStatus`, `QcApproval`. QC gate and first-stage release per DS 9.2. `docs/DOMAIN_MODEL_DEMO_SCOPE.md` lists every rule with its source and the guards the specification does not state (G1-G11).
- Tests in `tests/ERP.MES.Domain.Tests/Requests/` (request number, customer, priority, request/key/items, item route).

### Files changed
- New: the Domain types above, six test files, `docs/DOMAIN_MODEL_DEMO_SCOPE.md`. Modified: `docs/DECISION_LOG.md` (one Unresolved bullet), `docs/SESSION_LOG.md`.

### Decisions newly approved
- None. No existing decision was changed.

### Unresolved questions / risks
- G1-G11 in `docs/DOMAIN_MODEL_DEMO_SCOPE.md`. Request status transitions, route revision, QC rejection/partial quantities, item-versus-stage priority and `ItemNo` case sensitivity stay open.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI result is reported in the PR.

### Recommended exact next action
- Review the PR and answer G1-G11 (or accept them as provisional); then persistence mapping follows the migration work.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (domain model, owner decisions G1-G11)

### Goal
- Apply the owner's review of PR #14: corrections for G9, G10, G11 and the G6 priority decision in the specifications and Decision Log.

### Completed
- Decision Log #58 (G6 priority on `RequestItem`, G7 default `NORMAL`, and the other approved guards); Domain and Database Specification (sections 5.1, 18, 20 list and 32.2), Master and Architecture "remaining clarifications" updated.
- Code: removed the "no stage added after release" restriction (G9); `ApproveStage` renamed `ApproveStageWholeQuantity` and documented demo-only (G10); `AddItem` rejects cancelled and closed requests only, defaults to `NORMAL`, release limit documented as not enforceable yet (G11); `InternalsVisibleTo` for the domain tests and an internal `Request.SetStatus`.
- Tests revised and added; `docs/DOMAIN_MODEL_DEMO_SCOPE.md` section 3 now records the decisions.

### Files changed
- `src/ERP.MES.Domain/Requests/{Request,RequestItem,ItemStage}.cs`, `src/ERP.MES.Domain/ERP.MES.Domain.csproj`, `tests/ERP.MES.Domain.Tests/Requests/*`, `docs/DOMAIN_MODEL_DEMO_SCOPE.md`, `docs/DECISION_LOG.md`, the three specifications, `docs/SESSION_LOG.md`.

### Decisions newly approved
- #58 (numbering: #55 is in PR #11, #56 is reserved for the migration-tooling PR, #57 is in PR #10).

### Unresolved questions / risks
- Downstream-stage handling after a route revision, the production stage state machine, the request-status table, override-versus-priority-change precedence, enforcement of the release limit for items.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI result is reported in the PR.

### Recommended exact next action
- Review PR #14; after #11, #12, #13 merge I update the branch from `main` and rerun CI.

---

### Date / session
- Date: 2026-10-14
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (domain model, second owner review)

### Goal
- Three targeted corrections to PR #14: route-revision safety, Decision Log cleanup, QC-model wording.

### Completed
- `AddStage` became `DefineInitialStage`: initial route construction only, refused once a stage is released, documented as not route revision; the audited controlled revision remains unimplemented and unprohibited. Tests replaced (no unaudited post-release change, no revision-like method).
- Decision Log: duplicated ordering entries merged into one "Queue ordering" bullet (priority ties and Management-override precedence kept as open); stale priority-location wording removed; #58 G9/G10 reworded; #11 points to #58.
- `QcApproval` and `ApproveStageWholeQuantity` documented as demo-only and as not representing accepted/rework/scrap quantities; a test pins that.

### Files changed
- `src/ERP.MES.Domain/Requests/{RequestItem,ItemStage}.cs`, `tests/ERP.MES.Domain.Tests/Requests/*`, `docs/DOMAIN_MODEL_DEMO_SCOPE.md`, `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md`.

### Decisions newly approved
- None beyond the owner's corrections to the wording of #58.

### Unresolved questions / risks
- Controlled route revision and its downstream handling; production stage state machine; request-status table; override precedence; partial QC dispositions as production work.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI result is reported in the PR.

### Recommended exact next action
- Review PR #14; after #11-#13 merge I refresh the branch from `main` and rerun CI.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (SQLite DbContext and initial migration)

### Goal
- Add the SQLite DbContext, the mappings for customer, request, item, stage and QC approval, the initial migration generated by the pinned `dotnet-ef` 10.0.13 and its model snapshot, with persistence tests. No `EnsureCreated`, no SQL Server, no seed data.

### Completed
- `ErpMesDbContext` and entity configurations in `ERP.MES.Infrastructure`; `SqliteDatabase` and the migrations (`InitialCreate`, designer, snapshot) in `ERP.MES.Infrastructure.Sqlite`; design-time factory in `ERP.MES.Provisioning`.
- The migration was generated on the Windows CI runner (no SDK in this environment): a first attempt failed with "No DbContext named 'ErpMesDbContext' was found", the factory was moved to the startup project, the second run succeeded; the generated files were reconstructed from the printed base64 with their SHA-256 checked and committed unchanged. The temporary generation workflow was removed before the final head.
- Tests on real SQLite files (fresh creation, snapshot consistency, startup on an existing database, schema constraints, round trips across contexts, domain behaviour on reloaded objects) and guards (no `EnsureCreated`, migrations committed, Domain free of EF Core).
- `docs/PERSISTENCE_MAPPING.md` documents the mapping and the open mapping questions M1-M7.

### Files changed
- See the pull request; no package was added, so the licence inventory is unchanged.

### Decisions newly approved
- None.

### Unresolved questions / risks
- M1-M7 in `docs/PERSISTENCE_MAPPING.md`.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review the DbContext, migration, snapshot and tests; then the persistence services, seed data and reset follow in separate PRs.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (PR #16 review: row version and QC-stage integrity)

### Goal
- Add the application-managed row version (Decision Log #23) and a database-enforced reference from a QC approval to a stage of the same item, then regenerate the initial migration with the pinned tool.

### Completed
- `Version` shadow property (long, concurrency token) on customer, request, request item and item stage, set by `ErpMesDbContext.SaveChanges*`; QC approvals stay unversioned; `GetRowVersion`.
- `ItemStages` alternate key (RequestItemId, StageNo) and the composite foreign key from `QcApprovals`, preserving the surrogate keys.
- The initial migration and snapshot were regenerated on the Windows runner by `dotnet-ef` 10.0.13 and committed unchanged (size and SHA-256 verified); the temporary workflow was removed.
- Tests: four stale-update tests (customer, item, stage, request), version start/increment/no-op, no version on approvals, Domain free of the version, invalid and cross-item stage references rejected, append-only approvals accepted.
- `docs/PERSISTENCE_MAPPING.md` updated (section 2.1, M4 resolved, M8-M9).

### Files changed
- See the pull request. No package was added; the licence inventory is unchanged.

### Decisions newly approved
- None. #23 was already approved.

### Unresolved questions / risks
- M1-M3 and M5-M9 in `docs/PERSISTENCE_MAPPING.md`, notably per-row (not per-aggregate) concurrency.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review the corrected DbContext, migration, snapshot and tests.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01JCnmzjXbpMcTjwPcE9vsqQ (persistence services)

### Goal
- Implement the smallest application-facing persistence layer for the customer/request workflow, on the accepted DbContext, migration and SQLite configuration. No seed data, reset, authentication or screens.

### Completed
- `ERP.MES.Application/Persistence/`: `ICustomerRepository`, `IRequestRepository`, `Stored<T>`, `RequestSummary` and the persistence exceptions (no EF Core types).
- `ERP.MES.Infrastructure/Persistence/`: `EfCustomerRepository`, `EfRequestRepository` and the shared save/translation helper (provider-neutral); `SqliteDatabase.CreateContextFactory`.
- Tests on real SQLite files, new repositories per step: customer and request round trips, updates through Domain methods, Domain failures propagate and save nothing, duplicate business keys, missing customer/record, near-duplicate warning, request list, stale-version and in-operation concurrency conflicts; Application guards (no EF Core in source or in public signatures).
- `docs/PERSISTENCE_MAPPING.md` section 5 documents the design and its scope.

### Files changed
- See the pull request. No package was added; the licence inventory and the migration are unchanged.

### Decisions newly approved
- None.

### Unresolved questions / risks
- Per-row concurrency (M8): item-level and stage-level expected versions are not exposed yet. `UpdateAsync` takes a delegate (Action) so the Domain rules stay in the Domain; callers must not capture tracked state.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review the contracts, implementations and tests; then fictional seed data and reset in a separate PR.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (fictional demo seed data and safe reset)

### Goal
- Add repeatable fictional demonstration data and an explicit, guarded reset on the accepted persistence services. No sign-in, accounts, passwords, contract change or screens.

### Completed
- `DemoDataSeeder` (Infrastructure/Demo, provider-neutral): 3 customers, 6 requests, 12 items (6 MAKE, 6 REPAIR), 27 stages, 9 QC approvals, built only through the repositories and Domain methods; fixed data, repeatable (found by exact customer name / request business key).
- `DemoDatabase` (Infrastructure.Sqlite/Demo): `Inspect`, `InitializeAsync`, `ResetAsync`. Demonstration marker = SQLite header `application_id`; reset only on a marked database, everything else refused with the reason; replacement built in a temporary file and moved into place on success.
- Provisioning commands `demo-status`, `demo-init`, `demo-reset --yes`; a CI smoke step runs them on a temporary file.
- `Request.PlaceInStatusForDemonstration` (Domain, labelled demonstration-only) because the Domain has no public status setter and the transition table is unapproved.
- Tests (Domain, Infrastructure) and source guards; `docs/DEMO_SEED_AND_RESET.md`; `PERSISTENCE_MAPPING.md` and Decision Log Unresolved updated.

### Files changed
- See the pull request. No package was added; the licence inventory and the accepted migration are unchanged; no persistence contract changed (M8 stays documented).

### Decisions newly approved
- None. Two proposals await confirmation (Decision Log, Unresolved): the `application_id` marker instead of a marker table, and the demonstration-only status placement method.

### Unresolved questions / risks
- M8: before queue stage changes or QC actions, decide how the UI passes the expected version of the specific item or stage and add stale-update tests.
- The marker is SQLite-specific by design (demo database only).
- The Q1-Q6 `JalaliDateService` follow-up and its Decision Log record are still owed.

### Commands/tests actually run and results
- Not built locally (no .NET SDK in this environment, download blocked). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review the seed, the guard and the tests; then sign-in in a separate PR (approved authentication, PBKDF2, demo credential, bootstrap and recovery requirements, none simplified).

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (owner decisions on the seed-and-reset PR)

### Goal
- Apply the owner's two decisions on PR #18: approve the SQLite marker; make the demonstration status placement non-public.

### Completed
- `Request.PlaceInStatusForDemonstration` is now `internal`; the Domain project grants `InternalsVisibleTo` to `ERP.MES.Infrastructure` (besides the existing Domain tests). Assembly-wide access is the narrowest mechanism .NET offers; a source/reflection guard limits Infrastructure to this one internal member.
- Tests: method not public and Request has no public status member (Domain tests); Domain shares internals with exactly two assemblies; exactly one production call site (the seed); Infrastructure uses no other internal Domain method, constructor or setter.
- Decision Log #59 (marker) and #60 (status placement) recorded; the proposal removed from Unresolved; `docs/DEMO_SEED_AND_RESET.md` updated.
- Dataset, reset behaviour, migrations, persistence contracts and packages unchanged; M8 stays documented.

### Files changed
- See the pull request.

### Decisions newly approved
- #59 and #60 (above).

### Unresolved questions / risks
- M8 (item/stage expected versions before queue or QC actions); Q1-Q6 `JalaliDateService` follow-up and its Decision Log record.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge; then sign-in in a separate PR using the approved authentication, PBKDF2, demo credential, bootstrap and recovery requirements.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (sign-in and authentication)

### Goal
- Implement the approved authentication design without simplifying it for the demonstration: PBKDF2 hashing, sign-in with lockout, one-time administrator bootstrap, administrator recovery, last-administrator protection, default-deny authorization, audit, real fictional demo accounts with random one-time passwords, and reconcile the `CreatedByUserId = 1` placeholder. Separate from customer/request UI, queue, QC and the Jalali date-input decisions.

### Completed
- Application/Security (no Domain change): policy with safety floors, password and user-name rules, `AuthenticationService`, `BootstrapService`, `RecoveryService`, `AccountService`, `AuthorizationRules`; Infrastructure: PBKDF2 hasher, security unit on the DbContext, settings file loader, protected credential file; `AddAuthentication` migration generated by the pinned tool (initial migration untouched); Provisioning commands `migrate`, `issue-admin-token`, `issue-recovery-token`, `benchmark-hash`, `demo-*` (credentials never printed); sign-in window and view model; demo accounts created through the production use cases; the seed now uses real user ids; CI smoke step for the Provisioning commands with masked secrets.
- Documentation: `docs/AUTHENTICATION.md` (design, requirement mapping, assumptions A1-A8), updates to persistence mapping (M10, M11), demo seed notes, UI checklist section B, demo plan, Decision Log Unresolved.

### Files changed
- See the pull request. No package was added; the licence inventory is unchanged.

### Decisions newly approved
- None. Open for your confirmation (Decision Log, Unresolved): assumptions A1-A8, the Domain-versus-Application position of User/Role/Department, and foreign keys on the created-by columns (M10).

### Unresolved questions / risks
- M8 (item/stage expected versions before queue or QC actions); M10; the hash benchmark on the lowest-spec PC; sign-out and session timeout; user-management and authorization-calling screens; Q1-Q6 `JalaliDateService` follow-up. The sign-in window itself is not verified visually (checklist B).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review the design notes and the assumptions list; run checklist section B on the CI-built artifact; then decide M10 and the Domain position, before customer/request UI.

---

### Date / session
- Date: 2026-10-15
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (owner review of PR #19: architecture, A1-A8, M10)

### Goal
- Correct the architecture (identity concepts in the Domain), record the owner's decisions on assumptions A1-A8, and make the creator/approver columns real foreign keys with a safe upgrade path - keeping every approved security property intact.

### Completed
- Domain: `User`, `Role`, `Department`, `DepartmentCodes`/`RoleCodes` and `Username` (canonical key) in `ERP.MES.Domain/Identity`; no credential, cryptography, persistence or UI type there. Application: `UserCredential` and the use cases; Infrastructure: hashing, storage, mappings. Domain tests for the identity types; boundary tests rewritten to protect the real boundaries (the Domain may hold identity, never credentials or technology).
- A1 case-insensitive user names through one canonical key (Domain-computed, unique column); A4 one generic sign-in failure with equal password work for every case, locked attempts never extend a lock, distinctions only in the audit log; A6 codes (not labels) drive authorization, tested by renaming labels; A8 start-up uses `MigrateExistingAsync` (never creates); A2, A3, A5, A7 recorded.
- M10: foreign keys on `CreatedByUserId` / `ApprovedByUserId`; nullable only for the audit user and the pending recovery target. Older databases with placeholder actors are refused before the migration, unchanged; a demonstration database is replaced by `demo-reset`. The migration and snapshot were regenerated from the initial state with the pinned `dotnet-ef` 10.0.13.
- Decision Log #61-#71; Architecture and Domain/Database specification notes; `docs/AUTHENTICATION.md` rewritten (including exact local instructions for checklist section B); persistence mapping, demo, checklist and demo-plan updates.

### Files changed
- See the pull request. No package was added. CI artifacts are still published only from pushes to `main`.

### Decisions newly approved
- #61-#70 recorded from the owner's review; #71 records what stays open (session handling, M8).

### Unresolved questions / risks
- Session handling/sign-out and the user-management screens; M8; the hash benchmark on the lowest-spec PC; Unicode normalization of user names; database-level audit protection and the production migration policy; the sign-in window has not been checked visually (pending the owner's run of checklist section B).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Run checklist section B with the instructions in `docs/AUTHENTICATION.md`; then review the architecture, migration, security behaviour and tests.

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (password-policy change, separate PR after the merge of PR #19)

### Goal
- Change the password policy only: minimum 6 characters, white space forbidden anywhere, no composition rules, one policy for every place a user chooses a password.

### Completed
- `PasswordRules.Validate` rejects any space character (new `PasswordPolicyError.ContainsSpace`) and counts the minimum in characters; `AuthenticationPolicy` default 6 and safety floor 6; Desktop `appsettings.json` 6; sign-in window message for a space. Bootstrap, recovery, new account, administrator reset and own change all call the same rule; sign-in does not.
- Tests: boundary 5/6, no composition rules, space at start/middle/end, no trimming or normalisation, each entry point (bootstrap, create, reset, own change, recovery), generated demonstration passwords valid under the policy, view-model message. Two existing tests used passwords that were valid only under the old rule and were adjusted.
- Docs: Decision Log #72 (supersedes the length in #40), `docs/AUTHENTICATION.md`, Milestone 1 plan table, checklist B6.

### Files changed
- See the pull request. No package added, no schema change, no migration. Cryptography, lockout, tokens, audit, authorization and artifact publication untouched.

### Decisions newly approved
- #72.

### Unresolved questions / risks
- Whether "space" should also cover other Unicode space-like characters (no-break space, ideographic space); #72 covers U+0020 only. All earlier open items remain (sign-out/session handling, M8, hash benchmark on the lowest-spec PC, database-level audit protection, production migration policy, Q1-Q6).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge the PR; run checklist section B (B6 now accepts 6+ characters without spaces).

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (correction inside PR #20: reject all Unicode white space)

### Goal
- Refine the password rule of #72 from "space character" to "any Unicode white space"; nothing else.

### Completed
- `PasswordRules.Validate` rejects any character for which `char.IsWhiteSpace` is true (error renamed `ContainsWhitespace`); U+200C stays allowed; no trimming or normalisation; six-character minimum unchanged; sign-in still does not apply the policy. Sign-in window message names space, tab and line break.
- Tests: U+0020, U+00A0, U+3000, em/narrow no-break space, tab, LF, CR, VT, FF, NEL, line/paragraph separators rejected at start, middle and end; U+200C and decomposed/precomposed forms accepted as typed; entry-point tests (bootstrap, create, reset, own change, recovery) include non-space white space.
- Decision Log #72, `docs/AUTHENTICATION.md`, plan table and checklist B6 updated to the white-space rule.

### Files changed
- See the pull request. No package, schema, migration, hashing, lockout, token, audit or authorization change.

### Decisions newly approved
- #72 refined (white space instead of space only); this closes the "other space-like characters" question I raised.

### Unresolved questions / risks
- Unchanged from the previous entry. Invisible non-white-space format characters other than U+200C (for example U+200B) are accepted as ordinary characters.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge PR #20; run checklist section B.

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (JalaliDateService Q1-Q6 follow-up)

### Goal
- Close the open `JalaliDateService` questions Q1-Q6 with the owner's answers, add deterministic tests for them, and document them. No customer/request UI or other workflow.

### Completed
- Verified `main` after the merges of PRs #19 and #20; read the Master Specification (section 0.6, 27), Decision Log #8, #24, #45, plan section 3.1, the audit item U39 and `docs/JALALI_DATE_SERVICE.md`. Q1-Q6 had never been answered; they were put to the owner and answered (Decision Log #73).
- Code (`ERP.MES.Domain/Calendar/JalaliDateService.cs`): input accepts Arabic-Indic digits and ignores surrounding white space; everything else unchanged. The application already builds the service with Persian digits and `Iran Standard Time` (`App.xaml.cs`), so no composition change.
- Tests: Arabic-Indic and mixed digit shapes, surrounding white space (incl. no-break and ideographic space), inner white space and zero-width characters still rejected, white-space-only is `Empty`; new `JalaliIranTimeZoneTests` with hand-written expected values at Nowruz, 30 Esfand of a leap year, month and Gregorian-new-year boundaries, local-midnight UTC boundaries, Persian screen text, and the zone looked up by identifier (not the machine's zone). Existing tests that asserted the old rejections were changed deliberately.
- Golden dates: 33 pairs and the leap years re-checked with an independent algorithm (0 mismatches).
- Docs: Decision Log #73, `docs/JALALI_DATE_SERVICE.md` (sections 3, 4, 5, 7).

### Files changed
- See the pull request. No package, schema or migration; no change to authentication or any workflow.

### Decisions newly approved
- #73 (Q1-Q6).

### Unresolved questions / risks
- Digit shape in exports/Excel; pre-2022 daylight-saving treatment for old imported timestamps; golden dates are not from an official published table; earlier open items (sign-out/session handling, M8, hash benchmark, database-level audit protection, production migration policy).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge the pull request; then decide the next milestone (customer/request screens need session handling and M8 decisions first).

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (desktop session lifecycle, Decision Log #74)

### Goal
- Implement the owner-approved sign-out and session-lifecycle policy before any role-sensitive screen: inactivity lock, Windows lock/switch, manual lock, unlock with re-authentication, current authorization, sign-out, unsaved-work decisions, audit.

### Completed
- Application: `SessionService` (audit of sign-out, lock with reason, user switch, clean shutdown; `UnlockAsync` with one verification per attempt, existing counter/lockout, hash upgrade, user reload), `CurrentAuthorization` (reloads the user; default deny; used by every future role-sensitive command), `AuthenticationPolicy.InactivityLockMinutes` (default 15, 1-120, validated at start-up), new audit action codes.
- Desktop: `UserSession` (monotonic-clock idle timer, lock/unlock/sign-out/switch/close), `UnsavedWorkRegistry` and prompts (Save only where supported), `WindowsSessionMonitor`, `LockOverlay`, "Lock now" button and Ctrl+L, sign-out button, input hook, idle timer, shutdown handling in `App`.
- Tests: timeout boundaries and settings validation, manual lock, Windows events (including that resuming never unlocks), unlock success/failure/lockout/deactivation/must-change/role-and-department refresh, equal password work, hash upgrade, sign-out and unsaved-change decisions, switch user, best-effort shutdown audit, stale-principal authorization.
- Docs: Decision Log #74 (and #71/unresolved list updated), `docs/AUTHENTICATION.md` section 8a, Architecture Specification notes (17.1/17.2), checklist section C.

### Files changed
- See the pull request. No package, no schema change, no migration. Password hashing, lockout, bootstrap/recovery, default-deny authorization, last-active-Administrator protection and audit behaviour are unchanged; artifacts remain main-only.

### Decisions newly approved
- #74.

### Unresolved questions / risks
- M8 (item/stage expected versions) is still open for the queue and QC workflows. Production-only: central timeout storage, server-side session records, absolute maximum session length, per-role timeouts, database-level audit protection, production migration policy.
- The WPF parts (lock overlay, buttons, input hook, shutdown handling) are compiled and unit-tested through `UserSession` but have not been seen on a screen: checklist section C is pending the owner's run.
- Known limit: a lockout caused by wrong *current* passwords in the mandatory password-change form is not reported as such (same generic answer as elsewhere).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge the pull request, run checklist section C, then start the role-sensitive customer/request UI (its commands must call `CurrentAuthorization` first).

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (first customer and request workflows, Decision Log #75)

### Goal
- Replace the customer and request placeholders with functional screens on the approved persistence contracts, without inventing permissions or workflow.

### Completed
- Gaps found in the approved matrix were put to the owner and decided (#75): customer maintenance (Central, Planning), read access (every active user), the single transition CREATED to SENT_TO_MANAGEMENT, and the mixed-item workflow (Central header, Drawing MAKE, Planning REPAIR/any).
- Domain: `Request.SubmitToManagement` (CREATED only). Application: permissions `MaintainCustomers`, `ViewCustomersAndRequests`; `CurrentAuthorization.EvaluateAsync`; `CustomerService`, `RequestService` (create, list, view, submit, add item, capabilities), `Outcome`/`OperationStatus`/`ValidationIssue` codes, `QuantityText`. Infrastructure: `WorkflowServices`.
- Desktop: `CustomersView`/`CustomersViewModel`, `RequestsView`/`RequestsViewModel`, Persian `Messages`, page wiring per session. The screens decide nothing: buttons follow `Capabilities` from the Application and every command is checked again.
- Tests: Domain (submit), Application use cases against real SQLite (authorization boundaries per department, stale principals, validation, business key, near-duplicate warning, concurrency, persistence through new services, database failure), view models (empty/validation/denial/conflict/failure states).
- Docs: Decision Log #75, `docs/AUTHENTICATION.md` section 7, Domain/Database Specification 15.4 note, demo plan, checklist section D.

### Files changed
- See the pull request. No package, no schema change, no migration.

### Decisions newly approved
- #75.

### Unresolved questions / risks
- M8 (item/stage expected versions) still open: items are only added; nothing updates an item or stage. Editing request details (Planning) and items needs Domain methods first. Quantity input shapes, blank-optional handling and the Persian status wording are UI conveniences to confirm. An inactive customer is not refused for a new request because no rule says so.
- The WPF layer is compiled but not exercised by unit tests: checklist section D is pending the owner's run.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge the pull request, run checklist section D, then decide the next approved portion (request-detail editing needs a Domain method and the transition table).

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (inactive-customer rule, Decision Log #76)

### Goal
- Enforce, in the Application use case, that a new request is created only for an active customer, re-reading the customer's status at creation.

### Completed
- `RequestService.CreateAsync` reads the customer from the database when the request is created and refuses an inactive customer (`ValidationIssue.CustomerInactive`) or a missing one (`CustomerNotFound`, previously the `NotFound` status); nothing is created or changed. Persian messages and a list reload on the request screen. Existing requests of a later-deactivated customer are untouched.
- Tests: active succeeds; inactive refused through the service without the UI; deactivated after being listed but before creation; non-existent; database unchanged and no partial request; reactivation allows creation again; existing requests keep working; authorization is checked first; screen reload after the refusal.
- Docs: Decision Log #76 (and the open point in #75 resolved), checklist D18-D19. The earlier session-log entry that listed this as open is historical and left as written.

### Files changed
- See the pull request. No package, schema or migration; persistence contracts unchanged.

### Decisions newly approved
- #76.

### Unresolved questions / risks
- Two steps (check, then save) remain because the persistence contract is unchanged: a deactivation within milliseconds between them can still let one request through (documented in #76). Everything else from the previous entry is unchanged (M8, request/item editing, the WPF layer pending the owner's run).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge; run checklist D18-D19 together with section D.

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (M8: item and stage expected versions, Decision Log #77)

### Goal
- Close M8: let callers supply the version they loaded for the specific item or stage being changed, before queue and QC actions are built.

### Completed
- `RowVersions.cs` (`StageRef`, `RequestVersions`, `VersionedRequest`, `RowVersionExpectations`); `IRequestRepository.FindWithVersionsAsync` and `UpdateRowsAsync(requestId, RowVersionExpectations, change)`; `EfRequestRepository` implements them (check before the change, then the existing `PersistenceSave`); `RequestDetail.Versions`.
- Inspection result: a stage operation can also change its item (QC approval) and the next stage; protection is per row, documented in #77 and `PERSISTENCE_MAPPING.md` section 5. No aggregate-level claim.
- Tests: `RowVersionTests` (current versions succeed; stale item; stale stage; one stale named row rejects everything; competing write between load and save leaves no partial change; request-level version does not detect item changes; missing rows; Domain rules still apply).
- Docs: Decision Log #77, `PERSISTENCE_MAPPING.md`, `AUTHENTICATION.md` section 11.

### Files changed
- See the pull request. No package, schema or migration change.

### Decisions newly approved
- #77.

### Unresolved questions / risks
- Per-row only: an aggregate-level version is still undecided. Queue and QC actions are not implemented; they must name the rows they change.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge; then the Management production queue.

---

### Date / session
- Date: 2026-10-16
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (read-only Management production queue)

### Goal
- Implement the initial Management production queue as a read-only workflow, report options for the open queue-order policy, and add no write control.

### Completed
- Verified `main` (merge commits of #19-#25 present; Decision Log #77 present; M8 resolved), SDK 10.0.401, `dotnet-ef` 10.0.13.
- Application: `ProductionQueueService` (permission check against the freshly loaded user, `ListAsync` + `FindWithVersionsAsync`, documented provisional display order, current stage and QC position, estimates, row versions); new permission `ViewProductionQueue` (first Management only as an unapproved assumption; superseded by the entry below: Management and Planning, Decision Log #78). `WorkflowServices.Queue`.
- Desktop: `ProductionQueueViewModel` and `ProductionQueueView` (Persian RTL, Persian digits, loading / rows / empty / permission-denied / conflict / database-error / unexpected-error states, "بارگذاری دوباره"); the "صف تولید" navigation item is now implemented. No write action exists (a test checks the view model's public surface).
- Tests: `ProductionQueueTests` (real SQLite: visibility, current stage and QC derivation, estimates, deterministic order and tie-breaks, row versions, permissions incl. deactivated and forged principals, empty, read-only proof, failure statuses), `ProductionQueueViewModelTests` (Persian texts and digits, states, loading, retry, failures), the permission matrix test and the shell navigation test updated.
- Docs: `docs/PRODUCTION_QUEUE.md` (scope, columns, states, assumption, display order, options T1-T4 and O1-O4 with a recommendation), Decision Log unresolved items, `PERSISTENCE_MAPPING.md`, `MANUAL_UI_CHECKLIST.md` section E.

### Files changed
- See the pull request. No package, schema or migration change; no persistence contract change.

### Decisions newly approved
- None at the time of this entry; the owner approved the visibility, the target queue order and the override precedence afterwards (next entry, Decision Log #78).

### Unresolved questions / risks
- Owner decisions on the queue-order policy and the viewing permission were requested (answered in the next entry).
- The queue loads one request at a time through the existing contracts; a read model is needed for large data. The queue is an item-level overview, not the per-unit queue of released stages, and not live (snapshot + reload).
- The WPF layer is compiled but not exercised by unit tests: checklist section E is pending the owner's run.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge; run checklist section E; decide the queue-order policy and the viewing permission; then stage progression and QC approval (using `UpdateRowsAsync`).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (PR #26 update: approved queue decisions, Decision Log #78)

### Goal
- Apply the owner's approved queue decisions to PR #26 without adding write controls, schema or per-unit queues.

### Completed
- Checked `main` (last Decision Log entry #77; PR #26 is the only open PR), so the new entry is **#78**.
- `ViewProductionQueue` now allows active users of the Management **and Planning** departments; all other departments are denied by default. Tests: permission matrix, both departments allowed, other departments / deactivated users / forged and stale principals / password-change-pending users denied, Planning sees the same overview as Management.
- The screen is labelled a read-only overview (not the authoritative operational queue), with the provisional display order and the "not live" snapshot note; the Persian permission-denied text names Management and Planning.
- Decision Log #78 records the approved visibility (implemented), the target unit-queue order (Planning position per released stage, FCFS tie-break, IN_PROGRESS / STOPPED-problem / WAITING categories, then priority, position, planned end, stable identifiers) and the override precedence (stays active until cleared; Planning change preserves both and flags review) as **approved but not implemented**. The "Who may view" bullet was removed from "Unresolved" and the "Queue ordering" bullet was rewritten to the details that remain open (it was one bullet in the file; the earlier diff showed it as a changed line). Domain Specification 15.4, 18.1, 18.2, 32.2, 32.3 and the open-questions list updated.
- `docs/PRODUCTION_QUEUE.md` and checklist section E updated.

### Files changed
- See the pull request. No package, schema or migration change; no write control.

### Decisions newly approved
- #78 (owner, 2026-10-22).

### Unresolved questions / risks
- Details listed under "Queue ordering" in the Decision Log (stopped/problem state definition, FCFS timestamp, planned-end source, uniqueness of Planning positions, override position semantics and scope, clearing the review flag).
- The per-unit released-stage queue, Planning positions and overrides are approved targets only; they need production units, stage states and a schema change in later PRs.
- The WPF layer is compiled but not exercised by unit tests: checklist section E is pending the owner's run.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review and merge PR #26 after running checklist section E; then stage progression and QC approval using `UpdateRowsAsync`, or the per-unit queue design, as the owner chooses.

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (limited stage progression and QC approval, Decision Log #79)

### Goal
- Implement the limited, demonstration-only stage progression and whole-quantity QC approval on the fixed seeded routes, with current-authorization checks, per-row expected versions, same-transaction audit and tests. No rejection, rework, scrap, external supply, route editing, overrides or Planning-position controls.

### Completed
- Verified `main` (merge commits of #19-#26 present; Decision Log #77 and #78 present; M8 resolved; queue visibility and the target order recorded).
- Application: `StageWorkService` (`PerformAsync` for Release / Start / SubmitForQc / ApproveWholeQuantity, `LoadAsync`, `GetCapabilitiesAsync`, `AvailableFor`), `StageTarget` (ids and every row version the entry was read with); `ProductionQueueEntry` now also carries the request row version and the stage versions; new permissions `ReleaseStage` (Planning) and `RunStage` (Production) as UNAPPROVED implementation assumptions (QC approval uses the existing `InspectOrCorrectQc`); audit codes `StageReleased`, `StageStarted`, `StageSubmittedForQc`, `StageQcApproved`.
- Persistence: `IRequestRepository.UpdateRowsAsync(..., audit, ...)` adds the audit entries to the same context, so the stage change, the QC approval and the audit row are one transaction. No schema or migration change.
- Desktop: «مراحل و کنترل کیفیت» page (`StageWorkView`, `StageWorkViewModel`; shared `QueueRowFormatter`), four buttons mirroring the Application's answer, Persian success/failure messages, reload after every action; navigation item replaces the old QC placeholder.
- Demo data: `demo.production` account added (eight accounts); CI smoke test, tests and docs updated for eight.
- Tests: `StageWorkflowTests` (real SQLite: full sequence, persistence across contexts, QC gate, per-department authorization, deactivated and forged users, capabilities, invalid and repeated actions, stale request / item / stage / previous / next versions, missing versions, not found, failures leaving no partial change incl. a failing audit row and a competing write, audit entries and no secrets) and `StageWorkViewModelTests`; permission matrix, shell navigation and demo-account tests updated.
- Docs: Decision Log #79 and an Unresolved item for the department mapping, `docs/STAGE_WORKFLOW_DEMO.md`, Domain Specification 15.4 note, `PRODUCTION_QUEUE.md`, `PERSISTENCE_MAPPING.md`, checklist section F.

### Files changed
- See the pull request.

### Decisions newly approved
- None. #79 records the owner's scope and enforcement direction; the Planning/Production department mapping is an assumption to confirm.

### Unresolved questions / risks
- Department mapping of release / start / submit (Planning / Production) and whether others (Management, a supervisor) may act.
- No request-status gate: contract / Management-approval gates and the request-status transition table are not approved, so releasing is not blocked by request status.
- No screen lists the audit entries or a per-stage history (stored and tested).
- The WPF layer is compiled but not exercised by unit tests: checklist section F is pending the owner's run.
- A seeded item whose first stage is already beyond PLANNED cannot be used to demonstrate the release step; the demo path must pick a planned item (seed has several states).

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Review, run checklist section F, confirm or change the department mapping; then decide the next milestone (Planning priority edit / Planning positions, QC rejection with accepted/rework/scrap, or the per-unit queue).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (PR #27 update: owner-approved demo-only permission mapping, Decision Log #80)

### Goal
- Record the owner's approval of the demo-only stage-action department mapping without finalizing the production authorization matrix, and keep the missing request-status / contract / Management-approval gates explicitly unresolved and labelled.

### Completed
- Checked `main` (last Decision Log entry #78) and the open PRs (only #27, which holds #79), so the new entry is **#80**.
- Decision Log #80: release = Planning (`ReleaseStage`), start and submit for QC = Production (`RunStage`), QC approval = QC (`InspectOrCorrectQc`), demo-only; #79 (5) now points to it. The "department mapping" unresolved assumption was removed and replaced by the still-open production eligibility question (request-status transition table; contract / Management approval versus production release) and by the undecided extras (other departments acting, correcting a wrong start).
- Code comments (no behaviour change) and the Domain Specification 15.4 note now say "approved, demo only"; the stage-work screen note and `docs/STAGE_WORKFLOW_DEMO.md` state that no request-status, contract or Management-approval gate is checked; checklist F19 added.

### Files changed
- See the pull request. No schema, package or behavioural change.

### Decisions newly approved
- #80 (owner, demo-only).

### Unresolved questions / risks
- Request-status transition table and the relationship between contract / Management approval and production release; the demo has no such gate.
- Whether Management or a supervisor may act on stages; who may correct a wrong start or submission.
- The WPF layer is compiled but not exercised by unit tests: checklist section F is pending the owner's run.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Run checklist section F, then review and merge PR #27; then decide the production eligibility rules before any production release workflow.

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (PR #27: owner acceptance of the demo stage workflow, Decision Log #81)

### Goal
- Record the owner's manual Windows acceptance (checklist F1-F18) and confirmation of the demo-only stage-action department mapping, without any functional change.

### Completed
- Checked `main` (last Decision Log entry #78) and the open PRs (only #27, holding #79 and #80), so the new entry is **#81**. The mapping itself was already recorded as #80; #81 records the owner's acceptance after the manual test and the confirmation of that mapping, and states again that the production request-status, contract and Management-approval gates are not implemented.
- Documentation only: Decision Log #81 (and a back-reference in #80), `docs/STAGE_WORKFLOW_DEMO.md` status, checklist section F heading (F1-F18 passed, F19 not yet run), PR description.

### Files changed
- Documentation only. No code, schema, package or behaviour change.

### Decisions newly approved
- #81 (owner).

### Unresolved questions / risks
- Request-status transition table; contract / Management approval versus production release (no gate in the demo).
- Whether Management or a supervisor may act on stages; who corrects a wrong start or submission.
- Checklist step F19 (demo-only note wording) has not been run by the owner.

### Commands/tests actually run and results
- Not built locally (no SDK). Windows CI results are reported in the pull request.

### Recommended exact next action
- Merge PR #27 after review; then decide the production eligibility rules before any production release workflow, or the next milestone (Planning priority edits and Planning positions, QC rejection with accepted/rework/scrap, or the per-unit queue).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (design-only PR: workflow redesign, requirements R1-R9)

### Goal
- Incorporate the owner's additional business requirements (raw-material inquiry, Planning resources, QC quantity types and individual-unit QC, costing, job reports, KPIs, shifts, overtime, 12-step lifecycle) into the specifications as a DESIGN ONLY change.

### Completed
- Inspected `main` (e4be913, PR #27 merge; last Decision Log entry #81; no open PRs; no earlier workflow-redesign request or PR existed in the repository, so the 12-step lifecycle in the owner's message was treated as the lifecycle already provided).
- New docs: `WORKFLOW_REDESIGN.md` (lifecycle, state tables, authorization matrix, gap analysis, staged plan, decision register U-01..U-40), `RAW_MATERIAL_INQUIRY_DESIGN.md`, `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md`, `QC_QUANTITY_MODEL_DESIGN.md`, `COSTING_DESIGN.md`, `JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md`.
- Annotated (not rewritten) the Master, Domain/Database and Architecture specifications; Decision Log #82 (requirements approved; designs not) and #83 (individual-unit QC supersedes the whole-quantity-only limitation for the future workflow; notes appended to #58 and #79), plus an Unresolved pointer.

### Files changed
- Documentation only. No code, schema, migration, package or behaviour change.

### Decisions newly approved
- #82 and #83 record the owner's requirements only. No recommendation is approved.

### Unresolved questions / risks
- U-01..U-40 in `WORKFLOW_REDESIGN.md` section 9 (notably U-01 request type per request vs per item, U-02 contract scope, U-03 who defines MAKE items, U-12 machine assignment authority, U-17 UOM vs quantity type, U-22 chargeable time basis, U-38 meaning of finished/sent).
- Existing approved decisions that the requirements conflict with were later resolved by decisions #84-#92 (WORKFLOW_REDESIGN section 2).

### Commands/tests actually run and results
- Not built locally (no SDK); documentation only. Windows CI results are reported in the pull request.

### Recommended exact next action
- Owner reviews the design and decides U-01..U-06 first (they gate the lifecycle PR), then authorizes staged PR 1 (QC units and quantity types) or PR 4 (request lifecycle completion).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (PR #28 completion: confirmed decisions #84-#92, classification of U-01..U-40, owner decision packet)

### Goal
- Complete the design PR #28: apply the nine owner-confirmed decisions, classify every open decision, consolidate the genuinely open ones, and keep the PR documentation-only.

### Completed
- Checked `main` (still e4be913) and open PRs (only #28); Decision Log numbers #84-#92 are new.
- Decision Log #84-#92 (single work type per request; contract determination for MAKE only; Planning defines all items/stages after the gates; PDF mandatory; Yes/No inquiry lines; Planning-owned resources and supervisor limits; QC-owned UOM/quantity types with base mapping and explicit set composition; actual-time costing; individual-unit QC confirmed); supersession notes appended to #2, #4, #34, #75, #58, #79 without erasing history.
- Rewrote `docs/WORKFLOW_REDESIGN.md` (lifecycle with single-type requests, authorization matrix, classification of all 40 items: 9 answered, 1 existing, 18 defaults, 12 with an open part; Excel import, migration, concurrency, demo seed/reset sections; staged plan in the owner's phase order). New `docs/OWNER_DECISION_PACKET.md` (D1-D8). Companion designs and the three specifications updated.

### Files changed
- Documentation only. No code, schema, migration or package change.

### Decisions newly approved
- #84-#92 (owner). Defaults in the redesign are NOT approved.

### Unresolved questions / risks
- D1-D8 in `OWNER_DECISION_PACKET.md`.

### Commands/tests actually run and results
- Not built locally (no SDK); documentation only. Windows CI results are reported in the pull request.

### Recommended exact next action
- Merge PR #28 after CI, then implement phase 1a (single-type requests, PDF before submission).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 1a: one work type per request, Decision Log #84)

### Goal
- Implement Decision Log #84: a request has exactly one authoritative work type (MAKE or REPAIR); items inherit it; mixed-type requests are impossible.

### Completed
- Domain: `Request.WorkType` chosen at creation; `RequestItem` no longer has a type; `AddItem` has no type parameter.
- Application: `CreateRequestCommand` carries the type (`WorkTypeInvalid` when undefined); `AddItemCommand` has none; the item permission is the one for the request's type (provisional map #34 unchanged until the item-definition phase); `RequestSummary` and the queue entry carry the request's type.
- Persistence: `Requests.WorkType`; migration `SingleWorkTypePerRequest` moves the column (a request takes the single type of its items; mixed or item-less requests have no derivable type); `SqliteDatabase.MigrateAsync` refuses such a database with `RequestWorkTypeInvalidException` before changing anything (demo databases: use `demo-reset`); the desktop and the demo tool report it.
- Demo seed: three MAKE and three REPAIR requests (12 items). Desktop: work-type choice on the create form, a type column and detail text, no type on the item form. Tests and the demo/manual-checklist/domain/persistence documents updated.

### Files changed
- Source, tests, one migration (with designer and snapshot), documentation. No new package.

### Decisions newly approved
- None (implements #84). The request PDF before submission (#87) is the next PR (phase 1a-ii).

### Unresolved questions / risks
- Desktop (WPF) tests cannot run on the Linux sandbox; they run on the Windows CI. The WPF screens were not exercised by a person.
- The migration's NOT NULL step is a second line of defence; the preflight check is the supported path.

### Commands/tests actually run and results
- Local (Linux, SDK 10.0.113 substituted for the pinned 10.0.401 only on this machine; `global.json` was not committed): Domain 381 passed, Application 5 passed, Infrastructure 413 passed; Desktop tests compile but cannot run here. Authoritative run: Windows CI on the PR head.

### Recommended exact next action
- Merge after CI, then phase 1a-ii (store the request PDF once, require it before submission).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 1a-ii: request PDF before submission, Decision Log #87)

### Goal
- Central must attach the request PDF before submitting the request to Management (#87), with the document stored once and linked (#35), the 20 MiB limit (#54) and an audit trail.

### Completed
- Domain: `StoredDocument` (PDF signature, exact size limit, file-name cleaning, SHA-256), `RequestDocumentLink` (snapshot, superseded not deleted), `Request.AttachRequestPdf`, `Request.HasRequestPdf`/`CurrentRequestPdf`; `SubmitToManagement` refuses without a PDF.
- Application: `DocumentPolicy` (default 20 MiB), `RequestDocumentService` (attach/replace with expected version and audit, view), `RequestPdfRequired` and PDF validation issues, `Capabilities.CanAttachRequestPdf`.
- Persistence: tables `Documents` and `RequestDocumentLinks` (foreign keys restricted), migration `AddRequestPdfDocuments`, `IRequestRepository.FindCurrentRequestPdfAsync`.
- Desktop: attach/replace and view buttons and PDF line on the request screen; submit enabled only with a PDF.
- Demo seed: fictional PDFs on five requests; the `CREATED` request has none.

### Files changed
- Source, tests, one migration, documentation. No new package.

### Decisions newly approved
- None (implements #87).

### Unresolved questions / risks
- Opening a PDF writes a copy to the current user's temp folder (not deleted by the application).
- Content-hash de-duplication and retention/deletion remain undecided (#35); the size limit is a policy object, a configuration-file key is not read yet.
- WPF buttons/dialogs were not exercised by a person; view-model tests cover the logic.

### Commands/tests actually run and results
- Local (Linux, substituted SDK on that machine only): see the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then phase 1b (request lifecycle: Management decision, forward to Drawing, Drawing result).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 1b: request lifecycle after submission)

### Goal
- Implement the owner's lifecycle steps 2-5: Management approves/rejects, a rejected request is reopened, Planning forwards to Drawing, Drawing returns its technical result, with a request workflow history and audit.

### Completed
- Domain: `RequestAction`, `DrawingOutcome`, `WorkflowActor`, `RequestWorkflowEntry` (append-only), Request transitions `Approve`, `Reject`, `Reopen`, `ForwardToDrawing`, `RecordDrawingResult`, `SendBackToDrawing`; `SubmitToManagement` now records a history entry.
- Application: `RequestWorkflowService` (permission per action from the database, expected request version, history + audit in one transaction, reasons mandatory, Drawing note rules), audited submit; capabilities for the screen.
- Persistence: `RequestWorkflowEntries` table (users and requests restricted), migration `AddRequestWorkflowHistory` (no backfill: history starts at the migration), append-only guard in the context.
- Desktop: action panel (approve, reject, reopen, forward, Drawing result with outcome, send back), reason box and history grid.

### Files changed
- Source, tests, one migration, documentation. No new package.

### Decisions newly approved
- None. Labelled defaults used and not approved: send back to Drawing (draft row 9), the permission mapping for forward/send back (Planning's "receives the request" permission), a Drawing note for every outcome except full acceptance. Clarification routing and the effect of unavailable material remain OPEN D1.

### Unresolved questions / risks
- Cancellation (draft rows 6, 10, 11) is not implemented. Items and permissions for item definition are unchanged (phase 4a).
- Existing requests that were already beyond `CREATED` have no history entries (the history starts with this migration).
- WPF screens were not exercised by a person; view-model tests cover the logic.

### Commands/tests actually run and results
- See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then phase 3a (QC masters: units of measure and quantity types) and 3b (Planning masters).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 3a: QC masters)

### Goal
- Implement the QC-owned definitions of units of measure and quantity types as distinct concepts with a controlled mapping to the base counting unit (Decision Log #9, #90).

### Completed
- Domain (`Domain/Quality`): `UnitOfMeasure`, `UnitKind`, `UnitConversion` + `UnitConversions.TryResolve` (reciprocal derived, no chaining), `QuantityType` (simple types and sets with explicit `QuantityTypeMember`s; immutable mapping), codes `A-Z0-9_-` up to 20 characters.
- Application: `QcMasterService` (QC-only maintenance with expected versions; read for every active user via the new `ViewQcMasterData` permission), validation issues, `IQcMasterRepository`.
- Persistence: tables `UnitsOfMeasure`, `UnitConversions`, `QuantityTypes`, `QuantityTypeMembers`; migration `AddQcMasterData` seeds PCS, PAIR, KG, MM, PAIR to PCS = 2 and the types PIECE and PAIR (no creator recorded); unique codes, restricting foreign keys.
- Desktop: "واحدها و انواع مقدار" page (QC edits, others read).

### Files changed
- Source, tests, one migration, documentation. No new package.

### Decisions newly approved
- None. Master-data audit entries are not written yet (they come with the Planning rates); open points of D2/D3 are unchanged.

### Unresolved questions / risks
- Items do not reference quantity types yet (phase 4a); QC cannot yet see where a definition is used, so retiring a type is unrestricted until then.
- WPF page not exercised by a person; view-model tests cover the logic.

### Commands/tests actually run and results
- See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then phase 3b (Planning masters: machines with stable codes, rate history, operators, assignments, qualifications; Warehouse material price).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 3a: QC masters)

### Goal
- Implement the QC-owned definitions of units of measure and quantity types as distinct concepts with a controlled mapping to the base counting unit (Decision Log #9, #90).

### Completed
- Domain (`Domain/Quality`): `UnitOfMeasure`, `UnitKind`, `UnitConversion` + `UnitConversions.TryResolve` (reciprocal derived, no chaining), `QuantityType` (simple types and sets with explicit members; immutable mapping), codes `A-Z0-9_-` up to 20 characters.
- Application: `QcMasterService` (QC-only maintenance with expected versions; read for every active user via the new `ViewQcMasterData` permission), validation issues, `IQcMasterRepository`.
- Persistence: tables `UnitsOfMeasure`, `UnitConversions`, `QuantityTypes`, `QuantityTypeMembers`; migration `AddQcMasterData` seeds PCS, PAIR, KG, MM, PAIR to PCS = 2 and the types PIECE and PAIR (no creator recorded); unique codes, restricting foreign keys.
- Desktop: "واحدها و انواع مقدار" page (QC edits, others read).

### Files changed
- Source, tests, one migration, documentation. No new package.

### Decisions newly approved
- None. Master-data audit entries are not written yet (they come with the Planning rates); open points of D2/D3 are unchanged.

### Unresolved questions / risks
- Items do not reference quantity types yet (phase 4a); retiring a type is unrestricted until then.
- WPF page not exercised by a person; view-model tests cover the logic.

### Commands/tests actually run and results
- See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then phase 3b (Planning masters: machines with stable codes, rate history, operators, assignments, qualifications; Warehouse material price).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 3b-i: Planning machines and rates)

### Goal
- Planning registers production units and machines with stable codes and keeps the machine hourly-rate history (Decision Log #10, #82 R2, #89).

### Completed
- Domain (`Domain/Planning`): `ProductionUnit`, `Machine` (code and unit immutable), `MachineHourlyRate` (append-only, Rial per hour) and `MachineRates.RateOn` (rate in force on a work date).
- Application: `PlanningMasterService` (Planning registers; Planning and Management add rates; Warehouse reads rates; others cannot; every active user reads units and machines), new permissions `MaintainProductionResources`, `ViewPlanningMasterData`, `ViewMachineRates` (the last two labelled defaults), audited adds and rate changes, rate must start later than the machine's latest entry.
- Persistence: tables `ProductionUnits`, `Machines`, `MachineHourlyRates` (unique codes, one rate per machine and date, restricting foreign keys); migration `AddPlanningMachines`.
- Desktop: "ماشین‌ها و نرخ‌ها" page.

### Files changed
- Source, tests, one migration, documentation. No new package.

### Decisions newly approved
- None. Labelled defaults: Planning owns production units; rate-read audience (Planning, Management, Warehouse).

### Unresolved questions / risks
- Moving a machine to another unit is not possible (a history-keeping move is not built). Operators, assignments and qualifications are the next PR.
- Rial rounding and the exact decimal storage type remain open (#33); values are stored exactly.

### Commands/tests actually run and results
- See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then 3b-ii (operators, operator-machine assignments with history, qualifications) and 3b-iii (materials and Warehouse prices).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 3b-ii: operators, assignments, qualifications)

### Goal
- Planning registers operators, assigns them to machines and records qualifications, with history (Decision Log #82 R2, #89).

### Completed
- Domain: `Operator`, `OperatorMachineAssignment` (closed, never edited), `OperatorMachineQualification` (ended, never edited), `OperatorMachineRules` (assignment does not imply qualification).
- Application: `OperatorService` (register, rename, activate/deactivate, link to one active user, assign, close assignment, record/end qualification). Planning changes; Planning, Management and Production read (new permission `ViewOperators`, a labelled default). Assigning an operator with no current qualification succeeds with a warning (labelled default). All writes audited in the same transaction; per-row optimistic concurrency.
- Persistence: tables `Operators` (unique code, at most one operator per user), `OperatorMachineAssignments`, `OperatorMachineQualifications`; migration `AddOperators`.
- Desktop: "اپراتورها" page.

### Files changed
- Source, tests, one migration, documentation. No new package.

### Decisions newly approved
- None. Labelled defaults: `ViewOperators` audience; missing qualification is a warning, not a block.

### Unresolved questions / risks
- Supervisor behaviour (cannot change planned machine) is enforced by Planning-only maintenance; the supervisor role itself arrives with job reports (6b, D8).
- WPF page not exercised by a person; view-model tests cover the logic.

### Commands/tests actually run and results
- Local (Linux): Domain tests 473 passed; Infrastructure tests 483 run before adding the final operator test (one test then fixed, 13 operator tests passed); Desktop LocalLinux subset 115 passed. See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then 3b-iii (materials and Warehouse price per kg).

---

### Date / session
- Date: 2026-10-22
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 3b-iii: materials and Warehouse price per kg)

### Goal
- A Warehouse-owned material list and an append-only price-per-kg history in Rial (Decision Log #10, #91; `docs/COSTING_DESIGN.md`).

### Completed
- Domain (`Domain/Materials`): `Material` (stable immutable code, optional grade and form), `MaterialPrice` (append-only, Rial per kg) and `MaterialPrices.PriceOn`.
- Application: `MaterialService` - Warehouse maintains the list; Warehouse and Management add prices (existing `ChangeMaterialPricePerKg`); Planning, Warehouse and Management read prices; every active user reads the list. A price must start later than the latest entry; all changes audited in the same transaction.
- New permissions `MaintainMaterials`, `ViewMaterials`, `ViewMaterialPrices` (labelled defaults: Warehouse owns the list, U-08 recommendation; price-read audience).
- Persistence: `Materials`, `MaterialPrices`; migration `AddMaterials` (additive).
- Desktop: "مواد اولیه و قیمت" page.

### Decisions newly approved
- None.

### Unresolved questions / risks
- Which date selects the price for a cost (release date or calculation date) stays open (COSTING_DESIGN); `MaterialPrices.PriceOn` takes any date.
- Items and inquiries do not reference materials yet (phase 2a / 4a).

### Commands/tests actually run and results
- Local (Linux): non-Desktop tests (Domain 478, Application 5, Infrastructure 490) passed; Desktop LocalLinux subset 119 passed. See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then phase 2b (MAKE contract gate) or 2a (raw-material inquiry), per the staged plan.

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 2b: MAKE contract gate)

### Goal
- Planning decides whether a MAKE request needs a contract, prepares and submits it with a PDF; Management approves or rejects; Warehouse reads; production definition is blocked until the contract gate is satisfied (Decision Log #85, #93).

### Completed
- Domain (`Domain/Contracts`): `RequestContract` (decision, versions, PDF links, append-only history), `ContractEligibility`, `DefinitionReadiness`; `DocumentType.ContractPdf`.
- Application: `ContractService` (Planning: decide/change requirement/save draft/attach PDF/submit/start revision; Management: approve/reject with mandatory reason; read for Planning/Management/Warehouse), new permission `ViewContracts`, audit codes, validation issues.
- Persistence: `Contracts`, `ContractVersions`, `ContractDocumentLinks`, `ContractHistory`; migration `AddContractGate` (additive); every change re-reads the request facts and raises the request row version in the same transaction.
- Desktop: "قراردادها" page.
- Request statuses unchanged; readiness derived from persisted state.

### Decisions newly approved
- Owner direction (this task): rejection reason mandatory; rejection routing recorded as PROVISIONAL (Decision Log #93).

### Unresolved questions / risks
- No notification mechanism exists; approval is visible in the contract list (derived). Persistent unread notifications are not built.
- A contract decision made for one Drawing result is not re-validated against a later, different Drawing result except through the eligibility check (accepted result required).
- Raw-material disposition and item definition gates are not built; D1-D8 remain open.
- Contract search by number/date/customer is not built beyond the list.

### Commands/tests actually run and results
- Local (Linux): non-Desktop tests Domain 490, Application 5, Infrastructure 519 passed; Desktop LocalLinux subset 123 passed; Release build 0 warnings 0 errors. See the pull request for the final head's Windows CI totals.

### Recommended exact next action
- Merge after CI, then phase 2a (raw-material inquiry entities and Warehouse answers; routing after "No" stays behind D1) or 4a.

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 2a: raw-material inquiries)

### Goal
- Drawing asks Warehouse whether raw materials are available; Warehouse answers each line Yes or No (Decision Log #86, #88) - the parts that do not depend on owner decision D1.

### Completed
- Domain (`Domain/Inquiries`): `RawMaterialInquiry` with lines, append-only responses and history; request-status precondition.
- Application: `InquiryService`; permissions `ManageRawMaterialInquiries` (Drawing), `AnswerRawMaterialInquiry` (Warehouse), `ViewRawMaterialInquiries` (Drawing, Warehouse); audit codes; validation issues.
- Persistence: `RawMaterialInquiries`, `RawMaterialInquiryLines`, `RawMaterialInquiryResponses`, `RawMaterialInquiryHistory`; migration `AddRawMaterialInquiries` (additive).
- Desktop: "استعلام مواد اولیه" page.

### Decisions newly approved
- None. Labelled defaults are listed in Decision Log #94.

### Unresolved questions / risks
- D1 (clarification routing, effect of "No", result with an open inquiry) blocks the clarification thread, the disposition record and the production-release gate.
- D8 row 1 (Planning/Management read) is denied until decided; Planning therefore cannot yet see the material picture the design wants on the Drawing result.
- Lines use free-text grade/form, not the Warehouse material list.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI. Then phase 4a (item definition by Planning) is the next independent phase; it needs the answers to the questions listed in the final report of this session.

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 2a-ii: owner approval of D1-D8; D1 implementation)

### Goal
- Record the owner approval of D1-D8 (Decision Log #95) and complete the raw-material inquiry workflow that waited for D1.

### Completed
- PR #37 (phase 2a) merged after its final-head Windows CI (run 39977289354, Domain 498 / Application 5 / Infrastructure 534 / Desktop 169).
- Decisions recorded: Decision Log #95; `OWNER_DECISION_PACKET.md` status and per-item decision lines (original text kept); workflow redesign (gates, matrix, section 10); inquiry, costing, QC, planning and job-report designs annotated; Domain Specification note.
- Domain: dispositions and supply routes on inquiry lines, `DrawingClarification`; Application: `ClarificationService`, disposition and supply-route commands, `RequestGateService` (Management/Drawing/contract/inquiry/clarification gates, evaluated in the Application); new permissions; Planning and Management read inquiries (read-only).
- Persistence: `RawMaterialInquiryDispositions`, `RawMaterialInquirySupplyRoutes`, `DrawingClarifications`; migration `AddInquiryDispositionsAndClarifications` (additive).
- Desktop: "پرسش‌های نقشه‌کشی" page; disposition/supply-route actions and gate line on the inquiry page; the contract page shows the full gate.

### Decisions newly approved
- D1-D8 as recommended, with boundaries (#95).

### Unresolved questions / risks
- The D8 rows about acting on behalf of an operator and Planning corrections of stage starts/submissions cannot be enforced yet because the demonstration stage commands do not identify the operator; they are applied with phases 5a/6b.
- "Production users read only their own unit's queue" needs the user-to-unit designation (phase 5a); today the queue is Management/Planning only.
- The demonstration stage release still checks no gate (#80); phase 4a's item definition is the first command that calls the gate service.
- Detail chosen under D1 (not separately asked): a question that is answered but not yet closed keeps blocking release until Drawing closes it.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then phase 4a (Planning defines items and stages, gated).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 4a: Planning defines items and stages)

### Goal
- Move item and stage definition from Drawing to Planning, behind the Management / Drawing / contract / inquiry / clarification gates, with a validated specification (Decision Log #86, #95, #96).

### Completed
- Domain: `ItemSpecification` (quantity type, material, weight kg per base unit, length/diameter mm; exact scale, no rounding), `Request.DefineItem` (MAKE needs type, material, weight), stage `ProductionUnitId`/`MachineId`.
- Application: `ItemDefinitionService` (items and stages; gate evaluated in the use case; QC quantity types, Warehouse materials, Planning units and machines validated); `DefineMakeItems` is Planning's; `RequestService.AddItemAsync` removed.
- Persistence: nullable specification columns and restricting foreign keys; migration `AddItemSpecifications` (upgrade test keeps items, stages and QC approvals).
- Desktop: page "تعریف آیتم‌ها و مرحله‌ها"; the requests page lost its add-item form.
- Tests: item-definition service tests (gates, permissions, validation, concurrency, stale principals, stages), migration upgrade test, view-model tests; the queue / stage-flow fixtures now seed through the repository.

### Decisions newly approved
- None beyond #95; labelled defaults are listed in #96.

### Unresolved questions / risks
- Items and stages cannot be edited after creation (a controlled revision is not built).
- The Excel import (4b) needs its canonical example template and column documentation first; no compatibility with an unseen company workbook is claimed.
- The demonstration stage release still checks no gate (#80); the per-unit queue and release rules come with 5a.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then 4b: write the canonical import template and its column documentation, then the Excel import (preview, validation, transactional commit, conflict choice).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 4b: Excel item import)

### Goal
- Import items (and optional stages) from an Excel file with preview, validation, explicit conflict choice and one transaction (Decision Log #16, #97).

### Completed
- Domain log entities `ItemImport`/`ItemImportRow`; Application template, parser, `ItemImportService`; own bounded `.xlsx` reader and writer; EF repository, configurations, migration `AddItemImportLog`.
- `ImportItemsFromExcel` is Planning's only; gates enforced in the use case.
- Desktop page "ورود آیتم‌ها از اکسل" (template download, preview grid, per-row Skip/Update, commit).
- `docs/ITEM_IMPORT_TEMPLATE.md` (own canonical template, no company-workbook claim), Decision Log #97, checklist P.
- Tests: reader/writer (round trip, zip bomb, DTD, non-xlsx, numeric cells), parser, service (preview writes nothing, create, idempotence, conflict skip/update, locked, stages, invalid rows, permissions, gates, stale principal, stale version/file, REPAIR, 1,000 rows), migration upgrade, view model. The round-trip tests found and fixed a reader defect (rows after the first cell were lost).

### Decisions newly approved
- None beyond #95; labelled defaults are listed in #97.

### Unresolved questions / risks
- The company's real workbook has not been seen; the template may need a mapping.
- Items with a route cannot be revised by import; no background processing for very large files.
- Review pass before merge (Decision Log #98): the five-stage assumption was replaced by a 20-stage safety limit with an error for columns beyond it; atomic-rollback, numeric-cell and long-route tests added.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then 5a (per-unit queues; user-to-unit designation).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 5a: operational per-unit production queues)

### Goal
- Per-unit operational queues derived from persisted state: categories with reasons, Planning positions, Management overrides with review flag, gate/designation enforcement, audit and concurrency (Decision Log #78, #99).

### Completed
- Domain: stage `PlanningPosition`, `AvailableSinceUtc`, stop/resume with reason; `QueueOverride`, `QueueOverrideHistoryEntry`, `UserUnitDesignation`.
- Application: `UnitQueueService` (load, planning order, priority change with override review flag, override set/clear/confirm, stop/resume), `UnitDesignationService`, pure `UnitQueueOrdering`; `StageWorkService` now enforces gates, stop state and unit designation for stages that have a unit.
- Persistence: configurations, `EfUnitQueueRepository` (one transaction over several requests and overrides), migration `AddUnitQueue` with upgrade test.
- Desktop page "صف واحدهای تولید".
- Tests: domain, ordering, service (permissions, ties, persistence across restart, stale versions, gate failure, stopped work, overrides, review flag), migration, view model.

### Decisions newly approved
- None; labelled defaults are listed in #99.

### Unresolved questions / risks
- Planned end needs the calendar (6a). Operator assignment to stage actions and Planning correction of a wrong start come with 6b.
- The queue is read request by request; a read model is needed at larger volumes.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then 6a (working calendar and shifts; D5 rules).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 6a: Planning calendar, shifts and worker schedule)

### Goal
- Configurable working calendar, shifts, capacity, operator off-days and worker schedule with the D5 overlap rules, and a calendar-based planned-end forecast for the unit queues (Decision Log #13, #95, #100).

### Completed
- Domain: `ShiftTemplate`, `WeeklyWorkday`, `CalendarException`, `UnitShift`, `OperatorOffDay`, `WorkerSchedule`, pure `WorkingCalendar`; `IJalaliDateService.LocalToUtc/UtcToLocal`.
- Application: `CalendarService`, `WorkerScheduleService`, `WorkerScheduleRules` (D5), planned-end forecast in `UnitQueueService`.
- Persistence: six tables, seeded week, `EfSchedulingRepository` (rules checked in the insert's transaction), migration `AddPlanningCalendar` with upgrade test.
- Desktop: page "تقویم و شیفت‌ها"; planned end shown on the unit queue.
- Tests: domain calendar maths, service tests (permissions, templates, overlaps, exceptions, D5 scheduling rules, cancellations, forecast), migration, view models.

### Decisions newly approved
- None; labelled defaults are listed in #100.

### Unresolved questions / risks
- How headcount scales unit throughput (U-36); machine downtime registration by PM and its effect on the forecast (#14); effective-dated capacity.
- The Windows Infrastructure suite takes about 28 minutes; the job timeout was raised to 90 minutes in #41.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then 5b (quantity-level QC: accepted / rework / scrap per item and stage, D2/D3).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 5b: quantity-level QC)

### Goal
- QC decides base units as accepted / rework / scrap per item and stage, with the rework loop, shortfall decision and next-stage eligibility on accepted units (Decision Log #92, #95 D2/D3).

### Completed
- Domain: stage balances in base units, `RecordInspection`, `DecideShortfall`, `QcInspectionLine`, `ShortfallDecision`, new stage status `WAITING_SHORTFALL_DECISION`, `BaseUnitsPerUnit` on items.
- Application: `QcInspectionService` (worklist, history, inspection, shortfall decision with its own permissions); unit stages refuse the whole-quantity approval; quantity type base units captured at item definition and import.
- Persistence: columns and two append-only tables, `EfQcInspectionRepository`, migration `AddQuantityQc` with upgrade test.
- Desktop: page "بازرسی کنترل کیفیت"; the unit queue shows rework and the shortfall wait.
- Tests: domain invariants (no double counting), service (permissions, rework, scrap, shortfall, stale versions, history, audit), migration, view model.

### Decisions newly approved
- None; labelled defaults are listed in #101.

### Unresolved questions / risks
- QC corrections of earlier decisions and the Planning ordered-quantity raise are not built; partial production submissions and operator-reported quantities come with 6b; cost of rework and scrap is phase 7.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then 6b (daily job reports and supervisor review, D8).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 6b: daily job reports and supervisor review)

### Goal
- Operators report their own work per day; the unit supervisor (Planning when none, flagged) reviews; Planning may correct an erroneous start or submission with a reason (Decision Log #82 R5, #95 D8).

### Completed
- Domain: `DailyJobReport` with lines and append-only events, flags, `IsSupervisor` on designations, `UndoStart` / `UndoSubmit`.
- Application: `JobReportService` (draft, submit with overlap checks and flags, review), supervisor designation, `StageWorkService.CorrectAsync`.
- Persistence: three tables, `EfJobReportRepository`, migration `AddJobReports` with upgrade test.
- Desktop: page "گزارش کار روزانه".
- Tests: service (own-report rule, flags, review rules and fallback, visibility, validation, overlaps, corrections), migration, view model.

### Decisions newly approved
- None; labelled defaults are listed in #102.

### Unresolved questions / risks
- Late-report limit (no number given), amendment of approved reports, overtime links (6c), reports feed costing and KPIs only in phases 7 and 8.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then 6c (overtime assignments, D5).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 6c: overtime assignments and limits)

### Goal
- A supervisor proposes overtime, Planning approves it within limits it configured, Management decides what is outside them (Decision Log #89, #95 D5).

### Completed
- Domain: `OvertimeAssignment` (status machine, reason, times) and `OvertimeLimits`.
- Application: `OvertimeService` (propose, Planning decision, Management decision, cancel, limits, role-filtered load), `OvertimeRules` (booking rules and limit usage), new permissions and audit codes.
- Persistence: `EfOvertimeRepository` with rules checked in the saving transaction, two tables, migration `AddOvertime` with upgrade test.
- Desktop: page "اضافه‌کاری".
- Tests: domain state machine, service (flow, limits not configured, daily/weekly limits, authorization negatives, booking rules, cancel, stale versions, visibility), migration, view model.

### Decisions newly approved
- None; labelled defaults are listed in #103.

### Unresolved questions / risks
- Job report lines are not linked to assignments yet (planned with the KPI phase); the week start and "proposals do not reserve limit" are labelled defaults.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then phase 7 (costing, pricing, Warehouse receipt, finished/sent, dispatch; D3, D4, D7).

---

### Date / session
- Date: 2026-10-23
- Claude Code session ID (if useful): session_01UwqjPjBXLd2apjZb2urjds (phase 7a: item cost and price)

### Goal
- Warehouse calculates an immutable item cost from approved report time, machine rates and the material price; Management approves or changes the price (Decision Log #91, #95 D3/D4).

### Completed
- Domain: `ItemCostCalculation` / `ItemCostLine` (immutable snapshot) and `ItemPriceDecision` (Draft, Submitted, Approved; calculated or contract basis).
- Application: `CostCalculator` (formula v1, named gaps), `CostingService` (list, history, calculate, submit, approve), permissions and audit codes.
- Persistence: `EfCostingRepository` (inputs read in the saving transaction; superseded decisions refused), three tables, migration `AddCosting` with upgrade test.
- Desktop: page "هزینه و قیمت".
- Tests: pure formula tests, domain tests, service tests (flow, refusals with gaps, authorization negatives, price rules and stale versions, contract basis), migration, view model.

### Decisions newly approved
- None; labelled defaults (notably: the contract amount is entered by Management because contracts store none) are listed in #104.

### Unresolved questions / risks
- Where the contract amount should live (owner decision); Management adjustment lines; invalidation of a snapshot after QC corrections or amended reports; list pagination.

### Commands/tests actually run and results
- See the pull request for the Linux and Windows CI totals of the final head.

### Recommended exact next action
- Merge after CI, then phase 7b (Warehouse receipt after final QC, finished/sent after price approval, optional dispatch; D7).
