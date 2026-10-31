# #129 feat(api-dotnet): port blocked photos, the pauses they earn and the stale analysis check

Merged 2026-10-31.

## Summary

Ports the first half of the analysis slice to ASP.NET Core (ADR-0006): what happens to a blocked photo, the pause repeated blocks earn, the admins told about it, and the check for analyses that never finished. The Gemini adapter, the other half, follows in its own pull request.

- **A blocked photo** (US-091, FR-092–FR-095): in one transaction, the block is recorded in `photo_blocks`, the pause it earns is decided, each file gets a cleanup record (`PHOTO_BLOCKED`) and its row goes, the world fails as `BLOCKED` and the analysis counts toward the day. After the commit the admins are told and the files are removed from storage.
- **The pause** (V15, FR-098): a week at the third block in seven days; an admin's review when it happens again after a pause, or at once for suspected illegal material. The rule lives in `Moderation/Domain/SuspensionRules`, without the database (rule R1); the Worlds module reaches it through `ISuspensions` (rule R3).
- **Telling the admins** (FR-107): a pause waiting for an admin is emailed to every active admin, or to `ADMIN_ALERT_EMAIL` while there is none, without naming the learner. A pause that ends by itself is not emailed, and a mail server that refuses is logged while the pause stands. `ADMIN_ALERT_EMAIL` is a new optional setting, as in NestJS.
- **Analyses that never finished** (V16): released as `TIMED_OUT` after five minutes with their world, at startup and then every minute only while one may be running, so Neon can still suspend.
- **Migration `AdoptPhotoBlocks`**: empty. EF's generated migration matched Prisma's table, key, foreign key and index exactly.
- The integration tests' learner and world helpers, which were in two files, move to `Learners.cs`; `ApiHost` takes extra settings.

## What the checks caught

- **Unit tests: 4 `PipelineTests` failed.** The first version released lost analyses in `StartAsync`, which reads the database, so the API could not start without one. The release now runs at the start of `ExecuteAsync` and a failure is logged like a failed minute run. Recorded as a known difference in `COVERAGE.md`: NestJS releases before its first request, the .NET API while it starts answering.
- The 20 new integration tests passed on their first run. No contract test is new: the contract run's fake provider allows every photo, so this is checked in process, as `analysis.e2e-spec.ts` does through Prisma.
- Mutations, 10 of 10 caught:
  1. The pause decided before the block is recorded
  2. A level 1 pause emailed
  3. Earlier suspensions ignored
  4. Blocks counted over thirty days
  5. Suspended admins emailed too
  6. A blocked analysis recorded as failed
  7. An analysis starting does not wake the check (the first attempt did not compile; rewritten so it did, then caught)
  8. Startup does not watch
  9. A finished world failed by the release
  10. Suspected illegal material waits for the count

## Verification

| Check                                                | Result                           |
| ---------------------------------------------------- | -------------------------------- |
| `dotnet build` / `dotnet format --verify-no-changes` | 0 warnings, 0 errors / clean     |
| Unit / integration tests                             | 292/292 / 94/94                  |
| `pnpm test:contract --target dotnet`                 | 185/185, OpenAPI: No differences |
| `pnpm test:contract` (NestJS)                        | 185/185                          |
| `test:scripts` / `format:check` / `lint`             | 12/12 / pass / pass              |

`COVERAGE.md`: the analysis row leaves only the tutor, games and progress under a pause, which come with their slices.

## Before merging

Apply `AdoptPhotoBlocks` to staging's database (production deployment, section 17). It changes nothing, but the migration history must hold it.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
