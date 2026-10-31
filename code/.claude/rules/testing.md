# Testing Rules

- Start with the smallest relevant test suite; expand verification when the change crosses boundaries.
- Test business invariants, not implementation trivia.
- Add regression coverage when fixing a bug.
- Mock external AI/provider calls in unit tests; use controlled integration tests for provider adapters when needed.
- Do not mark tests as passed unless the command actually completed successfully.
- If a test cannot run because of missing infrastructure, report the blocker and test the highest-value deterministic parts available.
- For AI features, include evaluation examples for expected, edge, and failure cases.
