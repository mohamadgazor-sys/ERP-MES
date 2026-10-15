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
