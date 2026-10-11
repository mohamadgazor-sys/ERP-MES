# JalaliDateService (Milestone 1, step M1.2A)

| | |
|---|---|
| **Status** | Implemented for review in the M1.2A pull request. Behaviour below is what the code does; items in §7 are open questions for the owner. |
| **Location** | `src/ERP.MES.Domain/Calendar/` (interface `IJalaliDateService`, class `JalaliDateService`, types `JalaliDate`, `DateRange`, `DigitStyle`, `JalaliDateError`); tests in `tests/ERP.MES.Domain.Tests/Calendar/` |
| **Basis** | Decision Log #24 (storage conventions), #45 (`PersianCalendar` behind `JalaliDateService`); Master Specification section 0.6 and 27; plan section 3.1 |
| **Not part of this step** | `Money`/Rial formatting, rounding or storage scale; database mapping; request/item schema; Excel import; workflow; UI controls |

## 1. Purpose and rules it implements

All Jalali conversion, formatting and parsing happens in this one service. Nothing else in the code base converts calendars or builds Jalali text.

- **Storage convention (Decision Log #24):** calendar-only dates are `DateOnly`; timestamps are `DateTime` with `Kind = Utc`; there is no `DateTimeOffset`. Jalali and local time exist only in the text this service produces and reads.
- **Calendar engine (Decision Log #45):** `System.Globalization.PersianCalendar`, no extra package. Domain therefore still has no package references (guarded by an existing test).
- **No silent correction:** invalid input is reported with a specific error and a default value. A date such as `1405/07/31` is never changed to `1405/08/01`.
- **Jalali filters become Gregorian ranges** before querying (Master Specification section 0.6): half-open `[Start, EndExclusive)`; formatted Jalali strings are never compared.

## 2. API summary

| Member | Purpose |
|---|---|
| `ToJalali(DateOnly)` / `ToGregorian(JalaliDate)` | Convert a date. Throws `ArgumentOutOfRangeException` before Gregorian 0622-03-22 or for `default(JalaliDate)` (`ArgumentException`). |
| `GetJalaliYear(DateOnly)` | Jalali year of a stored date, for example for the Request business key. |
| `IsLeapYear(int)`, `GetDaysInMonth(int, int)` | Calendar facts; throw `ArgumentOutOfRangeException` for unsupported arguments. |
| `TryCreateDate(year, month, day, ...)` | The only way to create a `JalaliDate`; validates it. |
| `GetYearRange(int)`, `GetMonthRange(int, int)` | Whole year or month as a half-open Gregorian `DateRange`. |
| `TryGetRange(start, endInclusive, ...)` | A user's inclusive Jalali range as a half-open Gregorian range. |
| `FormatDate(JalaliDate)`, `FormatDate(DateOnly)` | `yyyy/MM/dd`. |
| `FormatDateTime(DateTime utc)` | `yyyy/MM/dd HH:mm`, 24-hour clock, in the display time zone. |
| `TryParseDate(string?)` | Parses `yyyy/M/d` text. |
| `TryParseDateTime(string?)` | Parses `yyyy/M/d H:mm` as local time and returns the UTC timestamp to store. |

The constructor takes **two required arguments, with no defaults**: the `DigitStyle` of produced text and the display `TimeZoneInfo`. Neither is decided yet (§7), so the caller must choose explicitly.

## 3. Supported formats

**Output**

| Value | Format | Example (Latin digits) | Example (Persian digits) |
|---|---|---|---|
| Date | `yyyy/MM/dd`, zero padded | `1405/07/19` | `۱۴۰۵/۰۷/۱۹` |
| Date-time | `yyyy/MM/dd HH:mm`, 24-hour, one space | `1405/07/20 00:43` | `۱۴۰۵/۰۷/۲۰ ۰۰:۴۳` |

Seconds are not shown; they are dropped, not rounded. Midnight is `00:00`, never `24:00`.

**Input**

- Date: year exactly 4 digits, month and day 1 or 2 digits, separator `/` only. Examples accepted: `1405/07/19`, `1405/7/9`, `۱۴۰۵/۰۷/۱۹`, and mixed digit shapes in one text (`۱۴۰۵/07/۱۹`).
- Date-time: a date as above, exactly one space, then hour 1 or 2 digits and minute exactly 2 digits, separator `:`. Example: `1405/07/20 9:05`.
- Digits: Latin `0-9` and Persian `۰-۹` (U+06F0 to U+06F9) are accepted. **Arabic-Indic digits (U+0660 to U+0669) are rejected.**
- White space around the text is not trimmed (a UI layer may trim before calling).

## 4. Invalid-input behaviour

Parsing and creation never throw for bad user text. They return `false`, set a `JalaliDateError`, and leave the output at `default`. Checks run in this order and the first failure is reported:

1. `Empty` — `null`, empty or white-space only.
2. `InvalidCharacter` — any character other than Latin or Persian digits and `/` (for date-times also one space and `:`). Includes `-`, `.`, `\`, letters, `T`, surrounding spaces in a date-only text, line breaks, and Arabic-Indic digits.
3. `InvalidFormat` — allowed characters, wrong structure: wrong number of parts, empty part, a year that is not 4 digits, a month or day of more than 2 digits, a time that is not `H:mm`/`HH:mm` (for example `10:5` or `10:30:15`), a missing or doubled space.
4. `YearOutOfRange` — year 0, or beyond the last supported year (the year that contains Gregorian 9999-12-31, where `DateTime` ends; the tests derive this limit from the calendar and do not hard-code it); also any date in a later month of that last year. A day after the last supported day *within its own month* is reported as `DayOutOfRange`, because `PersianCalendar` itself treats that month as ending at the last supported day (found by the first CI run of the tests, which had assumed otherwise).
5. `MonthOutOfRange` — month 0 or above 12.
6. `DayOutOfRange` — day 0 or a day the month does not have (31 in months 7 to 12, 30 Esfand in a common year).
7. `HourOutOfRange` (above 23) and `MinuteOutOfRange` (above 59) — reported after the date has been accepted.
8. `NonExistentLocalTime` / `AmbiguousLocalTime` — the local time falls in a daylight-saving gap or overlap of the display time zone; the service does **not** move it or pick one of the two instants.
9. `RangeEndBeforeStart` — `TryGetRange` only; the range is not swapped.

Programmer errors (typed values that cannot be right) throw: `ArgumentException` for `default(JalaliDate)` and for a non-UTC `DateTime` passed to `FormatDateTime`; `ArgumentOutOfRangeException` for unsupported years, months or dates passed to the non-`Try` members; `ArgumentNullException`/`ArgumentOutOfRangeException` from the constructor.

## 5. Golden data used by the tests

`PersianCalendar` is accepted unless a golden test fails against it (plan section 3.1). The pairs below are widely documented Iranian calendar dates; I wrote them from general knowledge and cross-checked their consistency with an independent arithmetic model of the calendar (all 33 rows agree), but I did **not** obtain them from a published table in this session. **Please review them or supply a source you accept.**

| Jalali | Gregorian | What it shows |
|---|---|---|
| 1300/01/01 · 1357/01/01 · 1380/01/01 | 1921-03-21 · 1978-03-21 · 2001-03-21 | Nowruz, older years |
| 1398/01/01 · 1399/01/01 · 1400/01/01 | 2019-03-21 · 2020-03-20 · 2021-03-21 | Nowruz before and after a leap year |
| 1401/01/01 · 1402/01/01 · 1403/01/01 | 2022-03-21 · 2023-03-21 · 2024-03-20 | Nowruz |
| 1404/01/01 · 1405/01/01 · 1406/01/01 | 2025-03-21 · 2026-03-21 · 2027-03-21 | Nowruz, current and next |
| 1397/12/29 · 1398/12/29 | 2019-03-20 · 2020-03-19 | Last day of common years |
| 1399/12/30 · 1403/12/30 | 2021-03-20 · 2025-03-20 | Leap day (30 Esfand) |
| 1402/12/29 · 1404/12/29 · 1405/12/29 | 2024-03-19 · 2026-03-20 · 2027-03-20 | Last day of common years |
| 1342/03/15 | 1963-06-05 | Well-known date |
| 1357/11/22 | 1979-02-11 | Well-known date |
| 1402/12/10 | 2024-02-29 | Gregorian leap day |
| 1403/09/30 · 1403/10/01 · 1403/10/12 | 2024-12-20 · 2024-12-21 · 2025-01-01 | Month boundary and Gregorian new year |
| 1403/12/01 | 2025-02-19 | Start of Esfand in a leap year |
| 1404/06/31 · 1404/07/01 | 2025-09-22 · 2025-09-23 | Shahrivar to Mehr |
| 1405/06/30 · 1405/07/01 · 1405/07/19 · 1405/07/20 | 2026-09-21 · 2026-09-23 · 2026-10-11 · 2026-10-12 | Current-year dates |
| 1405/01/02 | 2026-03-22 | Day after Nowruz |

Leap years asserted: 1375, 1379, 1383, 1387, 1391, 1395, 1399, 1403, 1408. Common years asserted: 1376, 1397, 1398, 1400, 1401, 1402, 1404, 1405, 1406, 1407, 1409.

Additional property tests: every day from 1900 to 2100 round-trips and advances by exactly one day; for every year 1300 to 1500 the distance between two Nowruz days is 365 or 366 and matches `IsLeapYear`, and the day after the last day of the year is Nowruz of the next year; month ranges tile each year 1380 to 1420 without gaps or overlaps.

**Future years caveat:** the least certain rows are the future ones (1406 to 1409 and the leap year 1408). I did not check them against an official published table for future years. If such a table ever disagrees with `PersianCalendar` for a year the business uses, a golden test will fail and a library would be proposed (plan section 3.1).

## 6. What is intentionally not here

- No `Money`, Rial formatting, rounding or storage scale (unresolved).
- No UTC-instant ranges for timestamp filters (needs the time-zone decision, §7). Date-only ranges are provided.
- No UI date picker, no database mapping, no use by request or item code.
- No `Result<T>` type (planned separately); the `Try...` members use an error enum.

## 7. Open questions for the owner (documented, not assumed)

| # | Question | What the code does meanwhile |
|---|---|---|
| Q1 | Which digit shape should appear on screen, Persian or Latin? (Audit U39.) | `DigitStyle` is a required constructor argument; both are tested. |
| Q2 | Which time zone is "local" for display and entry (for example Iran Standard Time)? | `TimeZoneInfo` is a required constructor argument; tests use fixed and test-only zones. |
| Q3 | Should input also accept Arabic-Indic digits, other separators (`-`, `.`), surrounding white space, or seconds? | All rejected with a specific error. |
| Q4 | Is `yyyy/MM/dd` with `/` the wanted display format, and should seconds ever be shown? | `yyyy/MM/dd` and `yyyy/MM/dd HH:mm`. |
| Q5 | For times that do not exist or occur twice (relevant for dates when Iran used daylight saving, up to 2022), is rejecting acceptable? | Rejected with `NonExistentLocalTime` / `AmbiguousLocalTime`. |
| Q6 | Are the golden dates in section 5 an accepted source? | Tests use them as written. |

Answers would be recorded as Decision Log entries; until then none of these is treated as decided.
