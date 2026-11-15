# Four interpretation decisions (from Decision Log #144-#146) - ALL DECIDED by the owner on 2026-11-15 (Decision Log #148)

> **Status: closed.** The owner approved: I-1 mandatory amount at submission to Management (optional while preparing); I-2 keep the receipt restriction; I-3 allow cancelling a never-submitted amendment with a reason; I-4 keep no late warning on amendments and preserve the original's lateness. The text below is the question and consequence analysis as it was put to the owner.

**What this is.** While implementing the owner's decisions of 2026-11-14, four details were not covered by the owner's text. Claude chose a behaviour for each so the work could continue. **These four are technical interpretations, not owner-approved requirements.** Each is easy to change. Nothing below reopens any decision recorded in #142. A one-line answer per row is enough ("keep" or the alternative).

| # | Question | Where it is recorded | Currently in the application |
|---|---|---|---|
| I-1 | Contract amount: optional or mandatory at submission? | #146 | **Optional** |
| I-2 | Can a receipt be reversed while a finished/sent mark depends on it? | #144 | **No** - reverse the mark first |
| I-3 | May an opened job-report amendment be discarded? | #145 | **No** |
| I-4 | Does an amendment show the late-report warning? | #145 | **No** |

## I-1 Contract amount at submission
- **Owner text (#142 A2):** Planning enters the amount; Management approves it with the contract; the price screen proposes it; Management may change the price with a reason. Nothing says submission is refused without an amount.
- **Optional (current).** Consequence: a contract can reach Management without an amount; Management approves a contract it cannot see a value for; the price screen then proposes nothing and Management types the price as before (older contracts keep working). Risk: the new control is skipped by habit.
- **Mandatory.** Consequence: every newly submitted contract carries an amount; the price screen always proposes; stricter control. Cost: a contract whose value is not known yet cannot be submitted; contracts already approved without an amount are unaffected (grandfathered). Effort: one check in `CheckSubmittable` plus test updates.
- **Recommended default: mandatory for new submissions.** It matches the intent of A2 ("approved with the contract", "the price screen proposes it"). Choose optional only if some contracts are legitimately submitted before their value is known.

## I-2 Reversing a receipt that a finished/sent mark depends on
- **Owner text (#142 B8, B9):** finished/sent needs both the price approval and the Warehouse receipt; a reversal needs a reason and Management's approval; the original is never deleted.
- **Blocked (current).** Consequence: two separate proposals and two approvals (mark first, then receipt), each with its own reason and audit trail; the system can never show a finished/sent item with a reversed receipt, so B8 always holds.
- **Cascade (alternative).** One proposal and one approval would reverse both. Consequence: fewer clicks, but one decision silently has two effects and a finished mark would be reversed without its own reason.
- **Recommended default: keep blocked.** It keeps B8 true at all times and each reversal explicit.

## I-3 Discarding an opened job-report amendment
- **Owner text (#142 B2):** reopening creates a new revision; the original and the full history are preserved; the revised report is approved again. Discarding is not mentioned.
- **No discard (current).** Consequence: if Planning reopens a report by mistake, the amendment stays; a second reopening of that report is refused while it exists; the operator can only complete it (typically with the same lines) and have it approved, which leaves the original superseded by an identical copy. History stays complete, but the correction is clumsy.
- **Cancel with reason (alternative).** Planning or the Production Manager cancels an open (not yet approved) amendment with a mandatory reason; it stays as history marked cancelled; the original remains the valid report and can be reopened again. Consequence: a small new action and status; no effect on KPIs or costing (they never counted an unapproved amendment).
- **Recommended default: allow cancellation with a reason**, history preserved. It removes a dead end and costs one small change.

## I-4 Late-report warning on an amendment
- **Owner text (#142 B1):** a report entered more than five working days after its work date shows a late warning; never refused.
- **No warning on amendments (current).** Consequence: an amendment is labelled as an amendment with its reopening reason; the late warning keeps meaning "the first report was late".
- **Warning on amendments (alternative).** Consequence: almost every amendment would show "late" (it is made after approval, usually more than five days later), which is noise and would hide real late first reports.
- **Recommended default: keep no warning.**

## Summary of recommendations
I-1 mandatory for new submissions; I-2 keep blocked; I-3 allow cancellation with a reason; I-4 keep no warning. Only I-1 and I-3 change the application (both small).
