# HANDOFF.md — ERP-MES project hand-off

Written 2026-11-02 for a brand-new Claude session that has no access to the previous account, conversation, memory or temp files. Everything here was verified against GitHub and the repository on that date unless marked **UNVERIFIED**. Where this file and the live repository disagree, **the live repository and GitHub win**; reconcile and say so.

Repository: `mohamadgazor-sys/ERP-MES` (https://github.com/mohamadgazor-sys/ERP-MES). Default branch `main`. Working branch used for all feature PRs: `claude/dazzling-goldberg-wdzbdp` (every PR head used this same branch name; each PR was merged with a merge commit, then the branch was fast-forwarded to the next work).

---

## Current state (updated 2026-11-05 evening, written in the last PR of the external-stage series; read this first)

| Item | Value |
|---|---|
| `origin/main` when this was written | `e9fd6ab9032e4a53ece49a93701c838db62952c5` (merge of PR #80). The PR that carries this text (external stages step 4) merges after it; check GitHub for the newest main. |
| PRs merged on 2026-11-05 | #73 (`715d6a6`), #74 (`00331c8`), #75 (`36b93d6`), #76 (`b8a373e`), #77 (`5bc430f`, owner decisions #129-#131, downtime clarifications), #78 (`d24071d`, external stages 1/4), #79 (`0c5ed06`, external stages 2/4), #80 (`e9fd6ab`, external stages 3/4). Each merged only after all six check runs were green on its PR head (`build-test`, `ui-smoke`, `sqlserver`, `offline-demo` x2; `generate-migrations` skipped as manual-only). |
| Post-merge runs on main | All four workflows success on `715d6a6`, `00331c8`, `36b93d6`, `b8a373e`, `5bc430f`, `0c5ed06`. On `d24071d` SQL Server, UI smoke and Offline demo succeeded but CI 42078891183 was **cancelled** (the workflow's `cancel-in-progress` on main when `0c5ed06` was pushed 42 minutes later); `0c5ed06` contains `d24071d` and its CI succeeded. On `e9fd6ab` the runs were in progress when this was written - not claimed. |
| Branches | Every remote work branch is contained in main or is the open PR's head; nothing unpushed; no stashes; one worktree. Nothing was deleted or force-pushed. |
| Implemented | Machine downtime (PR #74, #75; approved with clarifications #130, provisional forecast and pending visibility in #77). External supply / processing stages: unit category (#78), dispatches/returns/cancellations and QC hand-off (#79), «کار بیرونی» page, unit category and lead time on the screens (#80), Excel lead-time column and expected completion (the PR that carries this text). Decision Log #129, #132-#135. |
| Confirmed by the owner | Overtime is never booked inside a unit's effective regular hours (#127 point 5 -> #131), with tests for partial overlap, holidays, dated own hours and the re-check at approval. |
| Still open (owner) | How pieces a contractor never returns are closed (`docs/EXTERNAL_STAGES_DESIGN.md` section 5; proposal: Management's existing shortfall decision); rows of `docs/OWNER_REVIEW_PACKET_2.md` sections A, B (except B15), E. |
| Remaining deliverables | The final offline package from the final main (a new main needs a new package - see the history below); physical Windows-client verification (Windows 10/11 PC, a second disconnected launch, Ethernet unplugged, a machine without .NET) - none of these is done by automation. |

### Offline packages (history; each is tied to the commit it was built from - only a package built from the newest main is current)

The **final package** must be taken from the `Offline demo package` run of the final main after the last PR of this series merges (artifact `erp-mes-offline-demo`; record commit, file name, SHA-256 from the run log line `PACKAGE: ...`, run id, artifact id, size and expiry here). Packages of intermediate mains are not listed one by one.


| Built from main | Workflow run | Artifact id (size, expires) | ZIP and SHA-256 | Status |
|---|---|---|---|---|
| `8e737b7` | 41355359051 | 13103640562 (110,740,355 B, 2026-12-01) | `ERP-MES-OfflineDemo-2026.11.01-8e737b7-win-x64.zip`, `bcfea647381dc564cc589a580f6f1e5528b240347709272b567f539cf27238b0` | superseded (pre-#73 demo data) |
| `715d6a6` | 42018791380 | 13411072050 (110,747,858 B, 2026-12-05) | `ERP-MES-OfflineDemo-2026.11.05-715d6a6-win-x64.zip`, `22b0f552cb8bc4ca49e6c2385256d845fe3b20f6203e520e25e67c90e7704fe9` | superseded (no downtime) |
| `36b93d6` | 42049192821 | 13426716386 (110,815,360 B, 2026-12-05) | `ERP-MES-OfflineDemo-2026.11.05-36b93d6-win-x64.zip`, `f058a65c26fc65415ddc5436b5de9fd8bf095b8a574f41885714e28a9c4fd67f` | superseded (not the latest main) |
| `b8a373e` | 42060721266 | 13431117762 (110,815,199 B, expires 2026-12-05T16:08:59Z; artifact digest `sha256:f25518d705f9d9398219592d4840a8b994ba6e2ebb06efb0b540a1c4f2ee79c3`) | `ERP-MES-OfflineDemo-2026.11.05-b8a373e-win-x64.zip`, 106.1 MB, `8359e77cb75495b9a66e7a6ad212c61faa2fc1759db43141f58b01ceac924b4e` | superseded by later mains (each main push builds a new one: `5bc430f` run 42072331217, `0c5ed06` run 42085614493, and so on); it was current for `b8a373e`; proven offline from the ZIP on windows-2022 and windows-latest (all eleven accounts signed in, no network connection held). Code identical to `36b93d6` (PR #76 changed two documentation files). A later main needs a new package. |

### Test evidence (exact counts and the commit they ran on)

| Commit | Where | Result |
|---|---|---|
| `715d6a6` (main) | Linux sandbox, SDK 10.0.114, committed test projects | Domain 568, Application 41, Infrastructure 750 passed; WPF project compiles (`-p:EnableWindowsTargeting=true`, 0 errors) |
| `0dd56b1` (PR #74 head) | Windows CI `build-test`, SDK 10.0.401 | Domain 576, Application 41, Desktop 193, Infrastructure 754 = 1564 passed, 0 failed |
| `0dd56b1` | Linux sandbox | Domain 576, Application 41, Infrastructure 754 passed |
| `837f74b` (PR #75 head) | Windows CI `build-test` | all discovered tests passed (the CI step fails otherwise); counts in the run's TRX artifact 13418035623; provisioning smoke created 11 accounts |
| PR #75 working tree | Linux sandbox, **temporary harness, not committed** | 151 Desktop view-model tests passed (see below) |
| `f53a433` (PR #77 head) | Windows CI `build-test` green; local Linux: Domain 576, Application 41, Infrastructure 755; harness 151 | all passed |
| `0209e6e` (PR #78 head) | Windows CI green, SQL Server job applied `ExternalStages`; local: Domain 580, Application 41, Infrastructure 756; harness 151 | all passed |
| `f76dcbb` (PR #79 head) | Windows CI green, SQL Server job applied `ExternalShipments`; local: Domain 584, Application 41; Infrastructure 757 run with 1 failure (`DemoGuardTests`, fixed by renaming to `MarkCancelled` before the commit) and the affected tests re-run filtered (8 passed) | CI authoritative |
| `539f04a` (PR #80 head) | Windows CI green incl. `ui-smoke` opening «کار بیرونی» | all passed |
| step 4 working tree (steps 1-4) | Linux: Domain 584, Application 41, Infrastructure 758; harness 152; WPF, Desktop.Tests, SqlServer.Tests compile | all passed |

**SDK and Desktop tests - corrected facts.** The repository pins SDK **10.0.401** (`global.json`, `rollForward: disable`) and Windows CI uses it. The Linux sandbox of 2026-11-05 could only install Ubuntu's 10.0.114 and ran tests from a directory outside the repository so the pin was not changed; CI is the authority. There is **no** `tests/ERP.MES.Desktop.LocalLinux` project. The committed, reproducible Desktop tests are `tests/ERP.MES.Desktop.Tests` (`net10.0-windows`), which run on the Windows CI runner and can only be compile-checked on Linux. The "151 Desktop view-model tests on Linux" were run by a throw-away scratch project outside the repository that compiled the view models and those test files for `net10.0` (excluding `UserSessionTests`, which needs `Microsoft.Win32`); it is not part of the repository and its results are supporting evidence only.

---

## Reconciliation by the next session (2026-11-05)

Checked against GitHub and the repository on 2026-11-05. Where this differs from the sections below, this section is the current state; the sections below are kept as written on 2026-11-02.

| Item | Hand-off said | Found on 2026-11-05 |
|---|---|---|
| PR #73 | open, head `352fbf6`, three checks still running | Head still `352fbf67d48e639461fbe27e5af37470e87d364a`; all six check runs completed: `build-test`, `ui-smoke`, `sqlserver` (3/3 tests against a SQL Server 2022 container), `offline-demo (windows-2022)`, `offline-demo (windows-latest)` = success, `generate-migrations` = skipped (manual-only). **Merged** with a merge commit on 2026-11-05: main is now `715d6a659de6a6b721a5626abc54308e66ee4b49`. |
| SDK pin | `global.json` 10.0.113 | `global.json` pins **10.0.401** (`rollForward: disable`) and has since PR #6; Windows CI uses 10.0.401. 10.0.113 was presumably the previous sandbox's SDK. In this sandbox only Ubuntu's `dotnet-sdk-10.0` 10.0.114 is installable (the Microsoft download host is blocked by the proxy), so local tests are run from a directory outside the repository, which leaves the repository's pin untouched. |
| Old branches | "all earlier feature work is in main" | True. `claude/domain-model`, `claude/persistence-services`, `claude/m1-*`, `claude/phase-0-*`, `claude/ci-demo-artifact`, `claude/demo-shell`, `claude/four-day-demo-plan`, `claude/pdf-limit-20mib`, `claude/pkg-efcore-sqlite`, `claude/tooling-ef-design`, `claude/migration-initial`, `claude/erp-mes-phase-0-audit-6y4z44` are the heads of PRs #1-#17, all merged; their commits are not ancestors of main because those PRs were not merged with merge commits, so `git rev-list main..branch` counts are not a sign of lost work. `revert-7-claude/m1-2a-jalali-date-service` is PR #8, closed without merging (intended). Nothing was deleted. |
| Offline package | latest = `8e737b7` package (pre-#73 demo data) | **Superseded.** New package from main `715d6a6` (includes PR #73's demo data): workflow "Offline demo package" run 42018791380 (both jobs success, offline proof from the ZIP passed on windows-2022 and windows-latest), artifact `erp-mes-offline-demo` id 13411072050, artifact size 110,747,858 bytes, artifact digest `sha256:70fdc9f2b2c221eb1c3b34263b93533f13ae42b03159dfccd3c16a9ff8ff568a`, expires 2026-12-05T11:33:26Z. Inside: `ERP-MES-OfflineDemo-2026.11.05-715d6a6-win-x64.zip`, 106.0 MB, SHA-256 `22b0f552cb8bc4ca49e6c2385256d845fe3b20f6203e520e25e67c90e7704fe9`. Download: GitHub, Actions, run 42018791380, Artifacts (repository access needed). The owner's earlier physical test (Windows 11 Pro 25H2) was of an older package. |
| Main post-merge runs (`715d6a6`) | to be verified | SQL Server integration 42018791379, UI smoke 42018791490, Offline demo package 42018791380, CI 42018791324: all success. |
| Work merged this session | — | PR #74 (machine downtime register and forecast effect, HANDOFF reconciliation; merge `00331c8`, all four main runs green) and PR #75 (downtime screen, `demo.maintenance`, limitations page, `docs/EXTERNAL_STAGES_DESIGN.md`; merge `36b93d69be5480499c05b2ac8e8ff5f2c4ea4834`). Both merged only after all six checks were green on the PR head. |
| Offline package of `36b93d6` (superseded, see the package history above) | — | From main `36b93d6` (supersedes the `715d6a6` one): run 42049192821 (offline proof from the ZIP passed on windows-2022 and windows-latest; all eleven accounts signed in), artifact `erp-mes-offline-demo` id 13426716386, 110,815,360 bytes, digest `sha256:6834c84d1448366ee51f04521534e7a3729ec9972cbfc734d2ee225c2e3d39ef`, expires 2026-12-05T15:04:20Z. Inside: `ERP-MES-OfflineDemo-2026.11.05-36b93d6-win-x64.zip`, 106.1 MB, SHA-256 `f058a65c26fc65415ddc5436b5de9fd8bf095b8a574f41885714e28a9c4fd67f`. |
| Next task | — | External supply/processing stages (Master Specification §16): the owner's answers to the five questions of `docs/EXTERNAL_STAGES_DESIGN.md` section 3 (or "use the defaults"), then the four PRs of its section 4. Also open: Decision Log #127 point 5 and #128 (OWNER_REVIEW_PACKET_2 B15). |
| Session branch | — | This session works on `claude/admiring-mccarthy-mq8l0v` (created from main `8e737b7`, fast-forwarded to `715d6a6`). `claude/handoff-docs` was merged into it (merge commit, its two commits preserved) so that HANDOFF.md reaches main through this session's PR. |
| Worktrees / stashes | none | One worktree (the checkout), no stashes, clean tree. |
| `tests/ERP.MES.Desktop.LocalLinux` | listed as a project and a test command | Not in the repository (never committed; it was a local harness of the previous sandbox). `tests/ERP.MES.Desktop.Tests` targets `net10.0-windows`: on Linux it can only be compile-checked with `-p:EnableWindowsTargeting=true`; it runs on the Windows CI runner. README corrected. |
| "Limitations" page | not mentioned | The always-visible page «محدودیت‌های نسخه نمایشی» still says Excel import, contracts, Drawing, warehouse, PDF upload, calendar and costing are absent; all of these now exist. Stale text, to be corrected in a small PR. |
| Requirement gaps found so far | audit not done | **Machine downtime** (Master Specification §21; CLAUDE.md "PM reports machine downtime to Planning; downtime must affect the scheduling forecast and be auditable"; Decision Log #14) has a permission (`RegisterDowntime`, Maintenance/PM) but no entity, service, screen or forecast effect. **External supply / external processing stages** (§16) are not modelled (`ItemStage` says external-supply states are deliberately not present). Both are listed in the Session Log entry of 2026-11-05 with the proposed order of work. |

Local test runs on main `715d6a6` in this sandbox (SDK 10.0.114, Linux): see the Session Log entry of 2026-11-05.

---

## 0. Verified state at hand-off (2026-11-02)

| Item | Value |
|---|---|
| `origin/main` | `8e737b73639c952f0d6780ba7b440806e1f43412` (merge of PR #72) |
| Open PR | **#73** (demo data / docs / UI-test fix), head `claude/dazzling-goldberg-wdzbdp` @ `352fbf67d48e639461fbe27e5af37470e87d364a` — **NOT merged** |
| PR #73 checks at last query | `sqlserver` = success; `ui-smoke` = success (completed 02:21Z, so the ui-interact fix works on Windows); `generate-migrations` = skipped (manual-only job); `build-test`, `offline-demo (windows-2022)`, `offline-demo (windows-latest)` = **in progress at last query** (runs started 2026-11-02T02:14–02:15Z: CI 41376685293, UI smoke 41376685317, Offline demo 41376685360, SQL Server 41376685385) |
| Why not merged | Merge rule is "all required checks green". They were not finished, so by the owner's rule nothing was merged and nobody waited. The PR and branch are preserved. |
| Hand-off doc | this file, branch `claude/handoff-docs` (based on main 8e737b7) |

**First action for the next session:** re-query PR #73. If all six check runs on its head are green and the head is still `352fbf6` (or a descendant you have verified), merge it (merge method `merge`, pass the full 40-character `expectedHeadSha`). If `ui-smoke` or another check failed, read the job log and fix on the same branch (see §C for the previous failure of this kind).

Earlier PR #73 head `5212cc6` failed `ui-smoke` because `tools/ui-smoke/ui-interact.ps1` still asserted "expected 2 requests" while the demo data now seeds 3. Fixed in `352fbf6` (assertion changed to 3). The `ui-smoke` run on 352fbf6 then passed.

---

## A. Project overview and architecture

**Purpose.** A Windows desktop ERP/MES for IranTool (a machining / tool factory). The **Request** is the central business container: a customer's request (customer-supplied free-text request number) contains items; items are MAKE or REPAIR; items pass through production **stages** on **working units / machines**, with QC after every stage, planning/scheduling, contracts, costing, warehouse receipt and dispatch.

**Factory workflow (approved model).** Customer request → Management initial approval (+ contract if required, Planning creates, Management approves) → Drawing technical doability decision / raw-material inquiry (Warehouse Yes/No) → Planning defines items and stages (stage titles from a catalog) and releases the first stage → per-unit production queues → work sessions / daily job reports («ردیف کار») → QC of each stage (quantity-level accept/reject/rework; permanent reject = scrap) → next stage released only after QC approval of the previous one → finished/sent → Warehouse dispatch. External supply is a stage with dispatch/arrival tracking and lead time.

**Technology.**
- .NET 10 (SDK pinned in `global.json`: 10.0.113 [CORRECTED 2026-11-05: the pin is 10.0.401], `rollForward: disable`), WPF + MVVM, Persian UI, right-to-left. Windows 10/11 x64 only.
- EF Core 10.0.13. Dev/demo DB: **SQLite**. Production backend: **SQL Server 2022** (provider in `src/ERP.MES.Infrastructure.SqlServer`, **outside `ERP-MES.sln`**; has its own migrations and test project). Business logic must stay provider-independent.
- Layering: `Domain` ← `Application` ← `Infrastructure` ← `Infrastructure.Sqlite` / `Infrastructure.SqlServer`; `Desktop` (WPF), `Provisioning` (CLI tool for migrate/demo seed/admin tokens). Results use `Outcome<T>` / `ValidationIssue`; Persian message text in `Messages.cs`.
- Authorization: `AuthorizationRules` (default-deny, Department + Role); `CurrentAuthorization` reloads the user on every call. Audit-log entries are written in the same transaction as the change. Row-version concurrency. Filtered unique indexes.
- Dates: all user-visible dates Jalali (Shamsi), 24-hour time; DB stores Gregorian/native, converted at boundaries. Currency: Iranian **Rial only** — never silently converted to Toman.
- Projects: `src/` Application, Desktop, Domain, Infrastructure, Infrastructure.SqlServer, Infrastructure.SqlServer.Design, Infrastructure.Sqlite, Provisioning. `tests/` Application.Tests, Desktop.LocalLinux [does not exist - corrected 2026-11-05], Desktop.Tests, Domain.Tests, Infrastructure.SqlServer.Tests (CI only), Infrastructure.Tests, plus Shared/fixtures. `tools/` offline-demo, ui-smoke. `docs/` specs, Decision Log, Session Log, guides.

**Commands.**
```
dotnet restore ERP-MES.sln
dotnet build ERP-MES.sln -c Release          # needs Windows (WPF) for the Desktop project
dotnet test  ERP-MES.sln -c Release
# Linux: everything but the WPF UI is buildable/testable. Run per project:
dotnet test tests/ERP.MES.Domain.Tests
dotnet test tests/ERP.MES.Application.Tests
dotnet test tests/ERP.MES.Infrastructure.Tests
dotnet test tests/ERP.MES.Desktop.LocalLinux       # [CORRECTED 2026-11-05: this project does not exist; see 'SDK and Desktop tests - corrected facts' at the top]
# compile the real WPF project on Linux (compile check only):
dotnet build src/ERP.MES.Desktop -p:EnableWindowsTargeting=true
# demo database (credentials file must live OUTSIDE the repo):
dotnet run --project src/ERP.MES.Provisioning -- demo-init C:\erpmes\demo.db --credentials C:\erpmes\credentials.txt
dotnet run --project src/ERP.MES.Provisioning -- demo-reset <db> --yes ; ... demo-status <db>
$env:ERPMES_DATABASE='C:\erpmes\demo.db'; dotnet run --project src/ERP.MES.Desktop
# EF migrations: local tool `dotnet ef` 10.0.13 (see .config/dotnet-tools.json)
```
The Desktop app never creates, seeds or resets a database; only the Provisioning tool does.

**Migration workflow (important).** SQLite migrations are generated locally. The SQL Server project cannot restore NuGet packages in the previous sandbox (proxy 403), so SQL Server migrations are generated **in CI**: run workflow `SQL Server integration` (`sqlserver.yml`) manually (`workflow_dispatch`) with input `migration_name`; the job prints the generated files gzip+base64 in the log between `BEGIN_MIGRATIONS_B64` and `END_MIGRATIONS_B64`; decode, commit under the SQL Server project, push. This was done for PRs #64, #66, #67, #71 (titles "SQL Server migration for …"). A test asserts the SQLite and SQL Server migration/model sets stay consistent.

**CI workflows (`.github/workflows`).**
- `ci.yml` — `build-test` on Windows; on main also publishes a self-contained demo build artifact (14-day retention).
- `offline-demo.yml` — "Offline demo package", matrix `windows-2022` and `windows-latest`; builds and tests the offline ZIP (`tools/offline-demo`), firewall-isolated test; ZIP uploaded as artifact `erp-mes-offline-demo` only for push to main / manual run (30-day retention).
- `sqlserver.yml` — SQL Server integration tests against a throw-away container + the manual migration generator.
- `ui-smoke.yml` — launches the real WPF app on Windows, screenshots, plus the interaction script `tools/ui-smoke/ui-interact.ps1`.

---

## B. Approved business requirements

Source of truth order: `CLAUDE.md` (project rules, canonical decisions) → `docs/ERP_MES_CSharp_Master_Specification_Current_State.md` (business) → `docs/ERP_MES_CSharp_Domain_and_Database_Specification.md` → `docs/ERP_MES_CSharp_Architecture_Specification.md` → `docs/DECISION_LOG.md` → latest `docs/SESSION_LOG.md`. **Read `CLAUDE.md` first; its rules bind you** (stop and ask on conflicts; never invent business rules; no secrets in Git; run real builds/tests and report real results; update Session Log at the end of every substantial session).

The latest owner decisions are **Decision Log #121–#127** (read them in full; they are the authority for everything below). Earlier decisions #1–#120 cover the base model.

### B.1 Approved decisions (owner-approved, Decision Log #121–#126)
- **Request numbers**: customer-supplied free text; uniqueness = Customer + RequestNo + Jalali year; case-sensitive; never silently trimmed/case-changed.
- **Item numbering**: system item number `RequestNo-NNN` (`ItemSequence` int 1–999). The number is derived, never parsed back, never reused. Optional **customer item reference** is the only key for Excel re-import matching. Excel import: one mixed sheet, Persian headers, explicit Work Type column, ~1000 rows, preview/validation, transactional, manual conflict choice if a changed row already exists (no silent overwrite).
- **Assemblies**: items nest up to 4 levels; child numbers `.NNN`; parent may change only before stages exist; an assembly's first stage is released only when the release gate (children done/ready per #123) passes.
- **MAKE / REPAIR** per item (`WorkTypeID`). REPAIR requires: delivery date ≥ request date, warehouse code ≤ 40 chars; duplicate warehouse code is a **warning**, not an error; incomplete REPAIR items cannot be released.
- **Stage-title catalog**: Planning maintains it; titles are retired, never deleted; a stage snapshots the title text.
- **Item drawings**: PDF ≤ 20 MiB, revision history, uploaded by Drawing; explicit "open" button (no auto-open); one stored binary may be linked many times (store bytes once, explicit association rows).
- **Import permissions**: Planning imports full item definitions; **Drawing imports only technical columns, only while the request is `UNDER_DRAWING_REVIEW`** (#125 Q15).
- **Roles/departments**: new department **Production Manager** (`PRODUCTION_MANAGER`); Secretary label renamed (code stays `CENTRAL`). **Program Admin** administers users/accounts/master data/working-unit assignment (department/role vs working-unit assignment are separate concepts). Other permissions: QC alone inspects/corrects; Drawing alone decides technical doability; Planning creates contracts; Management alone approves/rejects contracts and initial requests; Warehouse alone receives items; Planning+Management edit priorities; Management+Planning maintain machine rates; Warehouse+Management maintain material price/kg.
- **Overtime**: approved by Management, or Production Manager within configured limits; beyond limits Management only; no self-approval.
- **Calendar**: recurring weekly unit schedules via `UnitShift.RegularDays` mask; dated own hours per unit; Friday normally off but **standing Friday 06:00–12:00 for chosen CNC units is an approved regular schedule**; holidays/off-days/shift patterns configurable (current 08:00–17:00 with 12:00–13:00 break; future 2 or 3 shifts) — never hard-coded as permanent.
- **Workflow**: QC approval of the preceding stage gates every later stage; QC correction after the fact preserves history, marks the item corrected, returns it to Stage 1, downstream handled manually. Priorities exactly `عادی`، `فوری`، `خیلی فوری`; only Management may override queue order, logged with old/new order and reason.
- **Actor history**: screens show who acted as display name + username + inactive marker (from the user id).
- **Terminology**: daily job report rows are «ردیف کار».
- **UI**: Latin codes rendered left-to-right inside RTL cells; buttons wrap rather than clip; Persian tooltips (164 documented in the Session Log); customer shown next to request number on screens combining requests.
- Reward/ranking formula is **Phase 2** (not built).

### B.2 Technical assumptions (made by Claude, not business decisions)
- Weekly-pattern overlap check for null `RegularDays` masks uses the company weekly pattern.
- Shift code length ≤ 20 characters (generated base-36 from the unit id).
- Demo data: fictional customers/requests; the three demo requests, nine+ accounts and passwords are generated into a credentials file outside Git.
- Timezone / Jalali conversion boundaries implemented in a date service (see `docs/`).

### B.3 Unresolved / awaiting owner
- **Overtime inside regular hours** (see §H): **ASSUMPTION recorded as Decision Log #127, NOT approved**.
- `docs/OWNER_REVIEW_PACKET_2.md` lists other defaults awaiting owner decision. Do not treat them as approved.
- The exact final wording of some tooltips and the screen-layout review need the owner's eyes on a real Windows screen (§F).

---

## C. PR history

Titles reflect the last commit of each PR; the feature scope per PR is approximate — use `git log` for exact contents. Merge SHA = the merge commit on `main`; "head" = last commit of the PR branch when merged. All of #65–#72 were merged only after all six checks were green on the **PR head** (`build-test`, `ui-smoke`, `sqlserver`, `offline-demo (windows-2022)`, `offline-demo (windows-latest)`, and the skipped `generate-migrations`). After merging, the main push re-ran CI/UI smoke/SQL Server/Offline demo; those were verified green for main 8e737b7 (distinct from PR-head evidence). Titles below are the last commit of each PR.

| PR | Merge SHA | Head SHA | Purpose |
|---|---|---|---|
| #62 | 79db558 | 5d982fe | Session log, offline demo package, interaction tests |
| #63 | 5704844 | da549e6 | Decision Log #121–#126, lifecycle/import-status docs |
| #64 | 4cecea8 | db282ba | SQL Server migration: Secretary label + Production Manager dept |
| #65 | 9f4c545 | 780662d | Users page, change-department, user directory, permission-aware navigation (#122) |
| #66 | 522c9f1 | fa166b0 | Item numbering, customer reference, assemblies (incl. SQL Server migration) |
| #67 | 60cf9d2 | eabad00 | REPAIR item fields (incl. SQL Server migration) |
| #68 | 5915b6f | 4739403 | Stage-title catalog, item drawings/revisions; SQL Server pass test registers stage title first |
| #69 | f8f8199 | 05b59bc | Drawings view model `System.IO` using fix (WPF compile) |
| #70 | 34342f7 | 0d5e24e | Import role split: Drawing technical columns only under Drawing review (#125 Q15) |
| #71 | 65809ff | 3c1c25c | SQL Server migration for unit-shift weekdays (calendar, Friday hours) |
| #72 | 8e737b7 | df9ff6b | Customer shown beside request number on combined screens; actor history |
| **#73** | **not merged** | 352fbf6 | Demo seed (3 requests, drawings, Friday shift), docs, ui-interact assertion fix |

PRs #1–#61 (earlier phases: skeleton, domain, persistence, request lifecycle, QC masters, machines, operators, materials, inquiries, items/stages, queues, calendar, QC rework, job reports, overtime, costing, warehouse, metrics, e2e acceptance, demo shell, UI smoke) are listed in `git log --first-parent main`.

Failures encountered and fixed (history, so you do not repeat them):
- #68 first `sqlserver` run failed: SQL Server pass test defined stage "فرز" with no catalog title → register via `PlanningMaster.AddStageTitleAsync` first. (Could not compile that project locally; verified by CI.)
- #69 first Windows run failed: handler placed in static `PromptWindow` (CS0708) → moved into `UnitQueueView`; missing `using System.IO;` in `ItemDrawingsViewModel` (WPF has no implicit usings) → added.
- `DemoGuardTests` matched the member name `Supersede` → renamed `ItemDrawing.MarkSuperseded`.
- Misc test fixes: migration-count expectations (26→30), `AuthorizationRulesTests`, DemoMasterData counts, Kind enum values (Off=1, Working=2).
- #73 `ui-smoke`: stale "2 requests" assertion → 3 (fix pushed, pending CI).

---

## D. Implementation status (12 areas)

| # | Area | Status |
|---|---|---|
| 1 | Users page / navigation (#65) | **Merged** (CI green on head; physical UI review pending) |
| 2 | Item numbering & assemblies (#66) | **Merged**; SQLite + SQL Server migrations merged; domain/infra tests passed |
| 3 | REPAIR fields & release validation (#67) | **Merged** |
| 4 | Stage-title catalog (#68) | **Merged** |
| 5 | Item drawings & revisions (#68/#69) | **Merged**; open-button behaviour needs physical Windows confirmation |
| 6 | Drawing vs Planning import separation (#70) | **Merged** |
| 7 | Working calendar / unit shifts / Friday (#71, earlier calendar PRs) | **Merged**; overtime-in-regular-hours interpretation unconfirmed (§H) |
| 8 | Production Manager / Program Admin permissions (#64–#65) | **Merged** |
| 9 | Workflow actor identification (#72) | **Merged** |
| 10 | Tooltips / LTR / layout / terminology | Mostly merged; **layout and tooltip wording not reviewed on a real screen** |
| 11 | Demo data & accounts | **Unmerged — in PR #73** (main still has the older 2-request demo seed) |
| 12 | Offline package | Package exists for main 8e737b7 but **predates #73's demo data**; a fresh package is needed after #73 merges (§G) |

Other original requirements not rechecked in this hand-off: a requirement-by-requirement audit against the Master Specification has **not** been done in this session; do it (§F).

---

## E. Test evidence

Honest summary — separate automated from physical.

Local Linux runs (sandbox; counts are as recorded during the sessions, historical, **not rerun after every later change**):
- P3 (numbering/assemblies): Domain 562, Application 41, Infrastructure 729, Desktop.LocalLinux 144.
- P4a: Domain 562, Application 41, Infrastructure 735, LocalLinux 144.
- P5: Domain 568, Application 41, Infrastructure 748.
- P6: Desktop.LocalLinux 150.
- P7 (demo): Infrastructure demo tests 45/45.
These were green at those points. Later changes were validated by CI on each PR head (all green), not by re-running every count locally.

CI evidence: every PR #65–#72 head had build-test, ui-smoke, sqlserver, offline-demo (×2) green before merge; main 8e737b7 push runs: CI 41355359213, Offline demo 41355359051 — both green; UI smoke and SQL Server runs for main also green.

**Caveats (do not hide):**
- An earlier P3 summary mentioned "nine failures"; that list could not be reconstructed. The final P3 and later Infrastructure runs were green.
- SQL Server tests cannot run in the previous sandbox; CI is the only evidence (first failure fixed in #68).
- WPF compile errors on #69 were found by Windows CI/local WPF compile, then fixed.
- WPF UI cannot be executed on Linux; `ui-smoke` (Windows CI) is the only automated UI evidence, plus the user's personal test (§G).
- No evidence exists for PR #73's head yet (checks running).
- Post-merge CI evidence exists for main 8e737b7; it will need repeating after #73.

---

## F. Roadmap (priority order)

1. Settle PR #73 (see §0). Merge only if all required checks are green.
2. Verify `main` after the merge: CI, UI smoke, SQL Server, Offline demo package workflows all green on the new main SHA.
3. Obtain the final offline package from the main run of `Offline demo package` (artifact `erp-mes-offline-demo`, ZIP `ERP-MES-OfflineDemo-<date>-<sha7>-win-x64.zip`). Record filename, SHA-256, commit SHA, artifact ID, run ID, size, expiry, download location. Artifacts expire in 30 days.
4. Physical Windows-client / offline validation on a real machine (second disconnected launch; Ethernet disconnected; fresh machine without .NET runtime).
5. Screen-layout and tooltip review by the owner (RTL, wrapping buttons, LTR codes, Persian wording).
6. Audit remaining original requirements (Master Specification) against the code; list gaps; propose focused PRs.
7. Obtain the owner's answer on the overtime interpretation (§H) and the open items in `docs/OWNER_REVIEW_PACKET_2.md`.
Keep PRs small and sequential; update the Decision Log and Session Log.

---

## G. Offline deployment evidence

Latest offline package (built from **main 8e737b7**, workflow run 41355359051, artifact `erp-mes-offline-demo` id 13103640562):
- File: `ERP-MES-OfflineDemo-2026.11.01-8e737b7-win-x64.zip`
- SHA-256 of ZIP: `bcfea647381dc564cc589a580f6f1e5528b240347709272b567f539cf27238b0`
- Size: 106.0 MB (653 files); artifact bytes 110,740,355; artifact digest `sha256:29a836abd32597f00bc63c7fc39318c10010aa36781600a94c21b1c4fcb1370c`
- Expires 2026-12-01T22:14:01Z. Download: GitHub → Actions → "Offline demo package" run 41355359051 → artifacts (needs repo access), or via the Artifacts API.
- Also: CI run 41355359213 published demo-build artifact 13104550211 (digest `1df440e8a3edb8a6ea1c7474081512e2ca90815591221501c918aef6f4f36488`, expires 2026-11-15).
- This package **predates PR #73**: it has the older demo data (2 sample requests; no Friday shift/drawing samples). After #73 merges, a new package must be taken from the new main run; the old one is superseded.

Automated offline evidence: the `offline-demo` workflow builds the ZIP, unpacks it, and runs it with network isolation (firewall) on windows-2022 and windows-latest. That is automated, not physical.

**Owner's personal physical test (verbatim):** "I personally tested the application on Windows 11 Pro 25H2 x64. It successfully launched and operated while Wi-Fi was disabled. I then enabled Wi-Fi, continued using the application, closed it, and relaunched it while connected."

What this proves: Windows 11 Pro 25H2 x64 initial disconnected launch and operation. What it does **not** prove: a second disconnected launch, an Ethernet-disconnected test, or behaviour on a machine without the .NET runtime / on Windows 10. It is also a test of an earlier package, not necessarily the latest.

---

## H. Overtime vs regular hours (open interpretation)

- **Approved**: a standing Friday 06:00–12:00 **regular** schedule for chosen CNC units.
- **Claude's interpretation, recorded in Decision Log #127, awaiting owner confirmation, NOT approved**: the system rejects an overtime assignment that falls inside a unit's regular hours (overtime applies only outside regular hours). The check runs after the operator/machine overlap rules.
- Do **not** mark it approved and do not change the behaviour. Ask the owner (the question is already framed in #127) before altering it.

---

## I. Technical risks and recovery

- **Branch dependencies**: every PR used the single branch `claude/dazzling-goldberg-wdzbdp`. Merges are merge-commits, so main contains all branch heads. `origin/claude/dazzling-goldberg-wdzbdp` = 352fbf6 = PR #73 head, a descendant of main 8e737b7. A pre-existing local copy of that branch in the original checkout is stale (f813de6) — ignore it, use `origin/...`.
- **Branch inventory**: remote branches are `main`, `claude/dazzling-goldberg-wdzbdp`, this `claude/handoff-docs`, and old merged feature branches (`claude/domain-model`, `claude/persistence-services`, `claude/m1-*`, `claude/phase-0-*`, etc.). All earlier feature work is in main. No stashes existed; the working tree was clean. A local-only branch `local/phase-10d-metrics` was patch-identical to main commit 78da047 (nothing lost). `local/p7` = pushed as the PR #73 head. Nothing valuable remained local-only at hand-off.
- **No force-push, history rewrite or branch deletion** was done in this hand-off. Do not delete old branches.
- **SQLite vs SQL Server model consistency**: any model change needs both migrations; SQL Server's must be generated by the CI manual job (§A). A consistency test fails if they drift.
- **PR-head vs merge-commit validation**: PR checks run on the PR head; the merge commit is only validated by the post-merge main run. Always verify main after merging.
- **Linux vs Windows**: WPF cannot run on Linux; only compile-checks (`EnableWindowsTargeting`). XAML/runtime issues show up only in Windows CI (`ui-smoke`) or on a real PC.
- **Offline-package provenance**: record SHA-256 + commit SHA + run ID; artifacts expire (30 days). Rebuild from main if expired.
- **Incomplete tests**: see §E caveats. No full-suite local rerun exists for the final state of main in this hand-off session; CI is the evidence.
- **Tooling quirks**: GitHub MCP server may disconnect (re-find tools with ToolSearch); `expectedHeadSha` must be 40 chars; avoid `pkill` of test hosts; use `git push` without force; `dotnet test` projects individually on Linux.
- **Recovery** if branches vanish: main (8e737b7) contains #65–#72. PR #73 content: `git fetch origin pull/73/head` (GitHub keeps PR refs even if the branch is deleted).
- Never commit credentials, real passwords, customer PDFs, production DB backups.

---

## FRESH-SESSION STARTUP PROMPT (copy everything in the block)

```
You are continuing the ERP-MES project (repo mohamadgazor-sys/ERP-MES, WPF/.NET 10, Persian RTL, SQLite demo + SQL Server). You have no memory of earlier sessions.

1. Read HANDOFF.md completely (it is on branch claude/handoff-docs and, once merged, on main). Then read CLAUDE.md, docs/DECISION_LOG.md (especially #121-#127), the latest docs/SESSION_LOG.md entry, and the three specifications in docs/.
2. Inspect GitHub and the repository directly: main SHA, open PRs (especially #73), check runs, branches, worktrees, stashes. Reconcile HANDOFF.md with the actual state and state any differences.
3. Preserve all branches and work: no force-push, no history rewrite, no deleting branches, no discarding work.
4. Identify the next safe task per the HANDOFF.md roadmap (settle PR #73 first; merge it only if all required checks are green on its head).
5. Continue with focused, sequential PRs. Implement, run automated tests (per-project dotnet test; SQL Server tests run in CI), debug failures, validate acceptance criteria, and report real results. Distinguish automated results from physical Windows/offline checks, and do not assign routine manual testing to the owner.
6. When all required checks pass on a PR head, merge autonomously (merge method merge, full 40-char expectedHeadSha), then verify main.
7. Do not ask questions already answered in the docs or Decision Log. Ask only for genuinely new business decisions. Treat Decision Log #127 (overtime inside regular hours) as an unconfirmed assumption.
8. Never commit secrets; update docs/DECISION_LOG.md and docs/SESSION_LOG.md as you work.
```
