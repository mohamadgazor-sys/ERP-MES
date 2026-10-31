# Item import: canonical example template (phase 4b)

Status: implemented in phase 4b (Decision Log #16, #97). **This is the application's own layout, designed from the written requirements. It has not been compared with any real company workbook, none has been inspected, and no compatibility with one is claimed.** If the company's real workbook differs, a mapping or a changed template is a separate, owner-approved change.

The application can write the template itself (page "ورود آیتم‌ها از اکسل", button "ذخیرهٔ قالب نمونه"). The two example rows in it are **fictional**; their codes (`PIECE`, `ST37-R30`, `SAW`, `MILLING`, `FCC-06`) must exist in the target database for the example to import.

## File rules

- Format `.xlsx` only (not `.xls`, not CSV). One worksheet is read: the first visible one; a second worksheet is reported and ignored. At most 20 MiB and about 5,000 rows (the requirement is about 1,000 items). Macros, external links and formulas are not evaluated; the cached value of a cell is read.
- The first non-empty row is the header row. Headers are matched **by name**, not by position, in any order. Matching ignores Arabic/Persian letter variants (ي/ی, ك/ک), Persian/Arabic/Latin digits, the zero-width non-joiner and repeated spaces. An unknown column is reported as a warning and ignored; a duplicate or a missing required column is an error.
- Cell text is trimmed. Nothing else about a customer reference is changed: it is case-sensitive and exact. System item numbers are assigned in file order: the highest sequence under the parent plus one, never reused; at most 999 items per parent (a larger file is refused as a whole).
- Everything is previewed first (nothing is written), then committed in **one transaction** or not at all.

## Columns

| Header | Required | Meaning |
|---|---|---|
| `نوع کار` | no | `ساخت` (MAKE) or `تعمیر` (REPAIR). If present it must match the request's type, otherwise the row is rejected; an empty cell means "the request's type". |
| `شماره آیتم مشتری` | no | The customer's own item number (Decision Log #121 Q2), free text up to 40 characters, unique in the file and per request, a **text** cell (a numeric cell is rejected because Excel would turn `1.10` into `1.1`). It is the ONLY key used to match an existing item on a re-import. A row without one is always a NEW item and is never matched by row number or description, so importing it again adds it again. The older header `شماره آیتم` is still accepted as the same column (not together with the new one). The system item number (`RequestNo-001`, children `...-001.001`) is assigned by the system and is not an import column. |
| `مرجع آیتم والد` | no | The customer reference of the assembly this item belongs to; the parent must be an earlier row of the file or already exist in the request, must be an assembly and must not already have stages; nesting is limited to four levels. |
| `مونتاژ` | no | `بله` marks the item as an assembly that may have children; empty means a normal item. |
| `تاریخ تحویل آیتم` | REPAIR rows | Jalali delivery date of a repaired item (for example `1405/02/16`); must not be before the request date. A MAKE row may leave it empty; a value that is given is checked the same way (Decision Log #124 B5). |
| `کد کالا انبار` | REPAIR rows | The warehouse's own code of the item, 1 to 40 characters, no leading or trailing space. A code that other items already carry is allowed and only reported as a warning when an item is defined by hand. |
| `نام قطعه` | **yes** | Part name. |
| `تعداد` | **yes** | Ordered quantity, a positive number (at most the Domain limit); in the unit of the quantity type. |
| `کد نوع تعداد` | MAKE | Code of an **active** QC quantity type (for example `PIECE`). Unknown code = row error. |
| `کد ماده` | MAKE | Code of an **active** Warehouse material. Unknown code = row error. |
| `وزن هر واحد پایه (کیلوگرم)` | MAKE | Raw-material weight in kg per base unit; at most 99,999,999.9999 with at most 4 decimals (more decimals are refused, never rounded). |
| `طول (میلی‌متر)` | no | Length in mm; at most 99,999,999.99, at most 2 decimals. |
| `قطر (میلی‌متر)` | no | Diameter in mm; same limits. |
| `اولویت` | no | Exactly `عادی`, `فوری` or `خیلی فوری`. Empty = `عادی`. |
| `مرحله k - عنوان`<br>`مرحله k - دقیقه برای هر قطعه`<br>`مرحله k - کد واحد`<br>`مرحله k - کد ماشین` | no | Stage groups k = 1, 2, 3, …, four columns each. The generated template shows 6 groups; a file may use **up to 20** (a safety limit against runaway files, not a business rule: the Domain has no limit on stages per item, and a route needing more is a documented change of `ItemImportTemplate.MaxStages`). A stage column numbered above the limit is an **error**, never ignored. Each group: title, planned minutes per piece (> 0), code of an **active** production unit, optional code of an **active** machine that belongs to that unit. A stage needs title, minutes and unit; stages must be contiguous (no stage 3 without stage 2). The planned quantity of a stage defaults to the item's quantity. |

For a REPAIR request the specification columns may be empty (a value that is given is still validated).

## What the import does

1. **Who/when:** only Planning, and only when the same gates as manual definition hold (Management approval, accepted Drawing result, MAKE contract decision, inquiry and clarification gates). The page only displays this; the Application refuses otherwise.
2. **Preview:** counts of new / unchanged / conflicting / invalid rows; every message carries its worksheet row number; all errors of a row are shown at once. A file with any error cannot be committed.
3. **Conflicts:** an item whose customer reference already exists in the request with different values is a *conflict*, shown field by field (old ← new). For each conflict the user must choose **Skip** or **Update**; there is no default and nothing is overwritten silently. An existing item that already has a stage or a QC approval is *locked*: it can only be skipped (updating an item that is already routed is not built). Stage columns apply to **new** items only; stage columns on a row of an existing item are reported and ignored.
4. **Commit:** the file is read again and must have the same SHA-256 hash as the preview, and the request must still be at the version that was previewed (otherwise: file changed / someone else changed the request, nothing is written). Items, stages, the import log (`ItemImports`, `ItemImportRows`: file name, hash, user, time, per-row outcome) and the audit entries (`ItemImportCommitted`, `ItemDefined`, `ItemRevisedByImport`) are written together or not at all.
5. **Idempotence:** importing the same file again writes nothing (all rows unchanged, no log entry).

## Not decided / not built

- Whether the company will use this layout, or a different real workbook, is open (needs the actual workbook).
- Revision of an item that already has a route; deleting items; more than 20 stages per item (safety limit, Decision Log #98); per-row stage quantities different from the item's.
- Import progress and cancellation for very large files: the reader is bounded (size, expanded size, rows), and 1,000 rows import in seconds, so background processing was not added.
