# Manual UI Checklist (demonstration build)

Approved in Decision Log #50 as the check for the shell until a UI-automation approach is chosen. Windows CI proves the application **builds** and the unit tests pass; it cannot show how the screens look. Run this list on a Windows 10 or 11 machine with a CI-built or locally built application and report what you see (a screenshot per failed item helps).

**Status:** the list covers the shell of PR "Demo shell and navigation". It grows with each screen. Nothing on it has been run by me; I cannot run WPF in my environment.

## A. Shell and navigation (this change)

| # | Check | Expected | Result |
|---|---|---|---|
| A1 | Start the application | One window titled "سامانه ERP/MES — نسخه نمایشی", centred, opens on the dashboard | |
| A2 | Layout direction | Navigation list is on the **right**; text is right-aligned; reading order is right to left | |
| A3 | Font | Persian text uses Tahoma (letters پ چ ژ گ ک ی join correctly; the half-space in "درخواست‌ها" and "محدودیت‌ها" shows as a gap without a visible box) | |
| A4 | Demo banner | Yellow banner at the top says the build is a demonstration with fictional data | |
| A5 | Navigation | Clicking each item changes the page and the heading; the selected item is highlighted | |
| A6 | Dashboard | Four tiles; each shows "—" and says no data has been loaded; no invented numbers | |
| A7 | Placeholder pages | "مشتریان", "درخواست‌ها", "صف تولید", "کنترل کیفیت" each say they are **not implemented yet in this build** | |
| A8 | Limitations page | Lists what the demonstration does not include | |
| A9 | Status bar | Shows the current Jalali date and time with **Persian digits** and 24-hour time, for Iran Standard Time (compare with a trusted Jalali calendar); it updates within about 30 seconds | |
| A10 | Resize | Shrinking to the minimum size and maximising keep all text readable; no overlap | |
| A11 | High DPI | At 125% and 150% scaling the text is sharp and nothing is clipped | |
| A12 | Close | Closing the window ends the process (no background process left in Task Manager) | |

## B. Added by later changes (not yet applicable)

Sign-in, customers, request entry, queue, QC approval, persistence across restarts and the reset command are added to this list by the PRs that build them.
