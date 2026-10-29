# ERP-MES
IranTool MES Program - a Windows desktop ERP/MES (WPF, MVVM, Persian right-to-left UI), .NET 10, SQLite for the demonstration and for single-user development, SQL Server 2022 provider and migrations for the later production database (Decision Log #117).

## Where to start
* **Presenting the demo without internet:** `docs/OFFLINE_DEMO_GUIDE.md` (the offline package is built by the `Offline demo package` workflow; see `tools/offline-demo`).
* **Business rules and decisions:** `CLAUDE.md`, the three specifications in `docs/`, `docs/DECISION_LOG.md`, `docs/SESSION_LOG.md`.
* **The demonstration walkthrough:** `docs/DEMO_WALKTHROUGH.md`; seed and reset: `docs/DEMO_SEED_AND_RESET.md`; what the automated UI test covers: `docs/UI_SMOKE.md`; manual checklist: `docs/MANUAL_UI_CHECKLIST.md`.
* **Open owner decisions:** `docs/OWNER_REVIEW_PACKET_2.md`.

## Developer quick start (Windows 10/11 x64, SDK pinned in `global.json`)
```
dotnet restore ERP-MES.sln
dotnet build ERP-MES.sln -c Release
dotnet test ERP-MES.sln -c Release
# a local fictional demo database (credentials go to a file OUTSIDE the repository):
dotnet run --project src/ERP.MES.Provisioning -- demo-init C:\erpmes\demo.db --credentials C:\erpmes\credentials.txt
$env:ERPMES_DATABASE = 'C:\erpmes\demo.db'
dotnet run --project src/ERP.MES.Desktop
```
The Desktop application never creates, seeds or resets a database; the provisioning tool does (`demo-init`, `demo-reset ... --yes`, `demo-status`). Linux can build and test everything except the WPF project (`tests/ERP.MES.Desktop.LocalLinux`).
The SQL Server provider (`src/ERP.MES.Infrastructure.SqlServer`) and its tests are outside `ERP-MES.sln` and run in the `SQL Server integration` workflow against a throw-away container.

## Safety
Everything in the demonstration is fictional. Never commit credentials, tokens, real passwords, customer PDFs or production database backups. The demonstration never connects to the company's existing systems.
