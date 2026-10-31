# #87 perf(api): let the database sleep while nothing needs it

Merged 2026-10-22.

## Summary

Neon's compute never suspended in production. The API's scheduled tasks read the database on every run, one of them every minute, and Neon only suspends after 5 minutes of inactivity. The smallest compute, 0.25 CU, therefore ran all day.

- **Usage so far:** the project page showed 36.29 CU-hours after about five days, roughly 6 a day.
- **Why it matters:** the free plan includes 100 CU-hours, and running out "suspends compute until the next billing period". At that rate the database would have stopped in the middle of every month.

Each task now reads the database only while it may have work. The process creates all of that work itself, a consequence of the single instance in A2, so it tells each task when work appears.

## Changes

| Task                             | Runs             | Reads the database                                                                               | Requirement kept              |
| -------------------------------- | ---------------- | ------------------------------------------------------------------------------------------------ | ----------------------------- |
| Release lost analyses            | every minute     | from an analysis starting until a minute past its 5-minute mark, and for 6 minutes after startup | V16, FR-021                   |
| Remove pending provider sign-ups | every 5 minutes  | for 21 minutes after a provider sign-up starts, and after startup                                | FR-090, U5 (15-minute expiry) |
| Remove deleted photos            | every 10 minutes | after a cleanup record is committed, until none are left; and every 6 hours regardless           | V10, NFR-009 (24 hours)       |

- **Where each task is told about work:**
  - A new analysis: `claim()`, the only place analyses are created.
  - A provider sign-up: Better Auth's `user.create.before` hook, through a new `onPendingSignUp` dependency.
  - Photo cleanup records: all five writers, after their transactions commit. These are world deletion, a failed upload, a blocked photo, and account deletion by the learner or by an admin.
- **Photo cleanup edge cases:**
  - The flag is cleared before a sweep, so a record committed during the sweep is not lost.
  - A failed sweep sets the flag again.
  - The six-hourly check catches a writer added later that forgets to call `expect()`.
- **Modules:** `AuthModule` and `ModerationModule` now import `StorageModule`. It depends only on Prisma, so this creates no cycle, and the comments that said moderation depends on nothing but the database are updated.
- **Docs:**
  - Production deployment, section 10, explains Neon's compute hours, how to watch usage, and that NFR-001 should be measured with the database awake.
  - The architecture overview, section 7, records the rule and its dependence on a single instance.
  - The analysis flow, the legal review and the security review no longer say "every 10 minutes".

## Trade-off

The first request after a quiet spell waits while Neon resumes the compute. Neon puts this at "a few hundred milliseconds".

## Test plan

- [x] Unit tests: 491 passed, 15 of them new. They cover: no reads without work, reads while work may exist, watching after startup, a record expected mid-sweep, a failed sweep, and the six-hourly check.
- [x] e2e against the local database: 305 passed
- [x] Typecheck, lint, `nest build`, `pnpm format:check`
- [ ] After deploy: Neon's monitoring graph shows the endpoint suspended during quiet hours, and daily compute falls well below 6 CU-hours

🤖 Generated with [Claude Code](https://claude.com/claude-code)
