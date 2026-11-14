# Production SQL Server - approved architecture (owner decision A4, 2026-11-14; Decision Log #142)

The architecture below is approved. The server, edition, instance name and deployment date are **IT's decision** and do not block application development.

| Requirement | Statement |
|---|---|
| Installation | A dedicated, supported SQL Server installation or instance on supported infrastructure; a supported modern version. |
| Existing system | The company's SQL Server 2008 R2 installation is never used or modified by this application. |
| Database | One dedicated database for ERP-MES. |
| Collation | Case-insensitive, accent-sensitive, with Persian-language compatibility. **Verified by integration tests** (see the SQL Server job; a collation test is part of this requirement). |
| Backups | Nightly full backups and frequent transaction-log backups, with restore testing. Owned and monitored by IT. |
| Connection string | Stored in protected per-machine configuration. Never committed to the repository. |
| Secrets | No production credentials or sensitive connection strings in source code or in Git. |
| Provider | The SQL Server provider and the committed SQL Server migrations exist and are tested in CI against a throw-away SQL Server 2022 container (Decision Log #117). |
| Open (IT) | Server, edition, instance name and deployment date. |

Collation test status: see `tests/ERP.MES.Infrastructure.SqlServer.Tests`.
