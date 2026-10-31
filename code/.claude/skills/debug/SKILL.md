---
name: debug
description: Runs a systematic root-cause debugging workflow for LanguZe instead of guessing or applying broad speculative fixes.
argument-hint: <problem>
---

# Debug Workflow

1. Reproduce or inspect the exact failure.
2. Capture the first meaningful error, not only the final symptom.
3. Identify the failing boundary: browser, API, DB, queue, realtime, storage, or AI provider.
4. Trace inputs and outputs across that boundary.
5. Form a small set of hypotheses and test the cheapest/highest-signal one first.
6. Apply the smallest root-cause fix.
7. Add a regression test when practical.
8. Re-run the reproduction and relevant checks.
9. Review the diff for unrelated changes.

Do not suppress errors, weaken validation, disable tests, or add retries without understanding the failure mode.
