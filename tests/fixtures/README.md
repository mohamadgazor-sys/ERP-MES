# Test fixtures

This is the only place in the repository where `.pdf`, `.xlsx`, `.xls`, `.xlsm` and `.csv` files may be committed (see `.gitignore`).

Rules:
- Fixtures must be **synthetic**: invented customers, request numbers, items and amounts. Never copy or anonymise a real customer file, contract, drawing, or production export.
- Keep files small and name them for the scenario they test.
- Do not create sub-directories named `data/` or `imports/` here; `.gitignore` ignores those names at any depth.
- Private/real files belong outside the repository or in the ignored directories `customer-data/` or `real-data/`.
