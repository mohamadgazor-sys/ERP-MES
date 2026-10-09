# ERP/MES Claude Code Starter Files

The starter files have been placed in the repository (Phase 0.1 housekeeping): `CLAUDE.md` at the repository root; specifications, logs and this file under `docs/`. Do not overwrite the three specifications with older versions. Keep the specs in `docs/` using these exact filenames:

- `ERP_MES_CSharp_Master_Specification_Current_State.md`
- `ERP_MES_CSharp_Architecture_Specification.md`
- `ERP_MES_CSharp_Domain_and_Database_Specification.md`

If they are not already in `docs/`, move/copy the latest versions there before starting Claude Code.

This starter package creates durable project-local memory:
- `CLAUDE.md` project instructions
- `docs/DECISION_LOG.md` approved rules and explicit unresolved topics
- `docs/SESSION_LOG.md` continuity between sessions
- `.claude/skills/` project-local skills tracked with the repository

These files are local files, not dependent on a Claude account. A valid authorized Claude Code login is still required to use Claude Code itself.

Do not commit credentials, passwords/tokens, production database files, customer PDFs, or real production data. Back up the repo locally and, if permitted by company policy, to a private remote repository.
