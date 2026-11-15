# Physical test of the offline package - guide for the owner

**Why this exists.** The automated `Offline demo package` workflow proves the package on **Windows Server** runners (windows-2022, windows-latest). It is **not** a test on a Windows 10/11 client, offline, by a person. Your earlier physical tests (2026-11-13: Windows 10 22H2 and Windows 11 Pro 21H2 on your own PC, offline) were of **older packages**; they do **not** validate the exact latest package and must not be quoted as doing so. The latest package needs its own short physical test.

## Recommended baseline
**Windows 11 Pro 25H2** (client edition, fully updated) is the recommended presentation and supported-client test baseline. Windows 11 22H2 is not on Microsoft's supported list for .NET 10; 24H2 or 25H2 is. Check with `winver` before you start. If the presentation computer is not 25H2 (or at least 24H2), update it with internet access **before** the day, then go offline.

## Which package
Use the package named as selected in `HANDOFF.md` ("Offline package - SELECTED FOR THE MANAGEMENT DEMONSTRATION"). Any `.zip` whose name or SHA-256 differs is a different build: do not use it.

## Short checklist (about 30 minutes)
Tick each box only after you did it. Write down anything unexpected.

1. **Verify the exact ZIP.** In PowerShell: `Get-FileHash .\<file>.zip -Algorithm SHA256` and compare with the value in `HANDOFF.md` and with the `.sha256` file beside the ZIP. They must be identical. If not, stop.
- [ ] checksum equals the recorded SHA-256
2. **Unpack** to a normal folder (for example `C:\ERP-MES-Demo`), not a network or cloud-sync folder. Run `Check-Demo.cmd`: no `FAIL` line.
- [ ] unpacked; `Check-Demo` shows no FAIL
3. **Go offline** before the first launch: Wi-Fi off and the Ethernet cable physically unplugged (or the adapter disabled and the cable out). Confirm the browser cannot open any site.
- [ ] offline for real
4. **Launch** with `Start-Demo.cmd`. The window title and header show the official name «برنامه جامع مدیریت و کنترل تولید ایران‌تول».
- [ ] opens offline; official name shown; Persian right-to-left
5. **Sign in** with each demo account from `DEMO-ACCOUNTS.txt` (11 accounts). Each sees only the pages its role may see.
- [ ] all 11 sign in
6. **Persistence.** As `demo.secretary` create a customer and a request; as `demo.management` approve it. Close the application completely.
- [ ] changes made and the application closed
7. **Relaunch while still disconnected.** The customer, the request and its approved status are still there.
- [ ] data survived the relaunch, offline
8. **Reset.** Run `Reset-Demo.cmd`; the demonstration data returns to the start (your test request is gone).
- [ ] reset works
9. **Rehearse the presentation** with `docs/MANAGEMENT_DEMO_GUIDE.md` and `docs/MANAGEMENT_DEMO_ACCEPTANCE_CHECKLIST.md`, including the live-only steps (section "Live versus seeded" of the checklist). Time it; note any screen that looks wrong.
- [ ] full rehearsal done, defects listed
10. **Leave nothing behind:** no credential file, no database backup, no real customer PDF on the computer after the demonstration.
- [ ] cleaned up

## What to report back
The Windows version (`winver`), the package file name and SHA-256 you verified, which boxes you could not tick and what you saw. Only then may the package be described as "physically tested on <date>, Windows <version>".
