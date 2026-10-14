# Dependency Licence Inventory

| | |
|---|---|
| **Date** | 2026-10-11 |
| **Scope** | Every direct and transitive NuGet package currently used by the three test projects (commit `45f54ec`, PR #6) |
| **Package set** | The resolved graph reported by `dotnet list package --include-transitive` in CI run [38175858405](https://github.com/mohamadgazor-sys/ERP-MES/actions/runs/38175858405) (identical to run 38175638879): 3 direct and 13 transitive packages. Versions are exactly those resolved; **no version was changed to produce this inventory.** |
| **Status** | For owner review. This is an engineering record of what the packages declare. It is **not legal advice and not a legal approval of any package's terms**; whether the company needs a separate legal acceptance of these terms is its own decision. |

## 1. Where these packages are used

All 16 packages are referenced only through `tests/Directory.Build.props` (three direct packages; the rest arrive transitively). No `src` project has a package reference, so **none of these packages is part of a project that would be shipped to factory PCs.** The notice obligations of MIT and Apache-2.0 normally arise when a package or its binaries are redistributed; if test outputs are ever distributed, the notice files named in §3 would have to go with them.

## 2. Inventory

Licence source for every row: the `<license type="expression">` element of the package's `.nuspec` (an SPDX expression), read from the `.nupkg` downloaded from `api.nuget.org` on 2026-10-11. All 16 packages also carry a repository signature file (`.signature.p7s`); its **presence** was checked, its cryptographic validity was not.

| # | Package | Version | Kind | Declared licence (SPDX) | Copyright / authors (from manifest) | Bundled licence or notice files | Flags |
|---|---|---|---|---|---|---|---|
| 1 | `Microsoft.NET.Test.Sdk` | 18.10.1 | direct | MIT | © Microsoft Corporation | none | F1 |
| 2 | `xunit.runner.visualstudio` | 4.0.1 | direct | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 3 | `xunit.v3.mtp-off` | 4.0.2 | direct | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 4 | `Microsoft.Bcl.AsyncInterfaces` | 6.0.0 | transitive | MIT | © Microsoft Corporation | `LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT` | F2 |
| 5 | `Microsoft.CodeCoverage` | 18.10.1 | transitive | MIT | © Microsoft Corporation | `ThirdPartyNotices.txt` (two copies) | F1, F2 |
| 6 | `Microsoft.TestPlatform.ObjectModel` | 18.10.1 | transitive | MIT | © Microsoft Corporation | none | F1 |
| 7 | `Microsoft.TestPlatform.TestHost` | 18.10.1 | transitive | MIT | © Microsoft Corporation | `ThirdPartyNotices.txt` | F1, F2 |
| 8 | `Microsoft.Win32.Registry` | 5.0.0 | transitive | MIT | © Microsoft Corporation | `LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT` | F2 |
| 9 | `System.Security.AccessControl` | 6.0.1 | transitive | MIT | © Microsoft Corporation | `LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT` | F2 |
| 10 | `xunit.analyzers` | 2.2.0 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson, marcind | none | F4 |
| 11 | `xunit.v3.assert` | 4.0.2 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 12 | `xunit.v3.common` | 4.0.2 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 13 | `xunit.v3.core.mtp-off` | 4.0.2 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 14 | `xunit.v3.extensibility.core` | 4.0.2 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 15 | `xunit.v3.runner.common` | 4.0.2 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |
| 16 | `xunit.v3.runner.inproc.console` | 4.0.2 | transitive | Apache-2.0 | Copyright (C) .NET Foundation; jnewkirk, bradwilson | none | — |

**Totals:** 16 packages — **7 MIT** (rows 1, 4–9) and **9 Apache-2.0** (rows 2, 3, 10–16). Missing licence: **0**. Unclear or non-SPDX licence: **0**. Licence given only by file or deprecated URL: **0**. Copyleft (GPL, LGPL, MPL, AGPL) declared: **0**. Proprietary or EULA-only: **0**.

## 3. Flags

**F1 — `requireLicenseAcceptance` is `true` (informational).** Four Microsoft packages (`Microsoft.NET.Test.Sdk`, `Microsoft.CodeCoverage`, `Microsoft.TestPlatform.ObjectModel`, `Microsoft.TestPlatform.TestHost`) declare that clients should ask for licence acceptance. Their declared licence is MIT, an ordinary permissive licence. The command-line restore does not prompt, and CI restored them without error. The owner's acceptance of this inventory is the natural place to record acceptance of those MIT terms.

**F2 — Bundled third-party notices (informational).** Five packages ship notice files naming additional components:
- `Microsoft.CodeCoverage` 18.10.1: Mono.Cecil 0.11.3 — MIT text (read in full).
- `Microsoft.TestPlatform.TestHost` 18.10.1: Newtonsoft.Json 13.0.3 — MIT; Mono.Cecil 0.11.3 — MIT; NuGet.Client 6.8.0.117 — Apache-2.0 (read in full).
- `Microsoft.Bcl.AsyncInterfaces` 6.0.0, `Microsoft.Win32.Registry` 5.0.0, `System.Security.AccessControl` 6.0.1: the standard .NET runtime `THIRD-PARTY-NOTICES.TXT` (about 44–48 thousand characters). I read its section headings (ASP.NET, Slicing-by-8, Unicode data, Zlib, Mono, ISO, Intel) and identified the licence families it contains (Apache, BSD, ISC, MIT, zlib); I did **not** read it line by line. Each package's own `LICENSE.TXT` is the MIT licence and matches its declared expression.
No copyleft or proprietary component was seen in any of the notice files read.

**F3 — Scope of obligations.** All 16 packages are used by test projects only (§1), so the application distributed to factory PCs does not include them.

**F4 — Analyzer package.** `xunit.analyzers` is a build-time analyzer, not a runtime library; it does not end up in an output the application would ship.

## 4. Conclusion for this package set

- Every package states a licence, in a machine-readable SPDX form; **no missing, unclear or potentially incompatible licence was found** among the 16 packages, and none uses a copyleft or proprietary licence.
- The set is limited to permissive licences (MIT and Apache-2.0), used only by test projects.
- Nothing in this inventory required changing a package version. A later version change would need a new inventory.

## 5. Method and limits

1. Package ids and versions: copied from the CI log of `dotnet list package --include-transitive`.
2. Metadata: each `.nupkg` was downloaded from `https://api.nuget.org/v3-flatcontainer/` into an isolated scratch directory and read as an archive; nothing from it was executed. The `.nuspec` `license`, `licenseUrl`, `copyright`, `authors`, `repository` and `requireLicenseAcceptance` fields were recorded, and licence-like files (`LICENSE*`, `NOTICE*`, `THIRD-PARTY*`, `ThirdPartyNotices*`, EULA) were listed and, where bundled third-party notices existed, read.
3. **Limits:** what a package declares is taken as stated; upstream repositories were not independently audited; individual binaries inside a package were not scanned for embedded code; the long .NET third-party notice file was not read line by line; legal compatibility with the company's intended use was not assessed by a lawyer; advisories are covered separately by the CI vulnerability check (plan §8.1).
4. The inventory is accurate for the versions above. It must be refreshed whenever a package or version changes (Decision Log #53).
