# LanguZe — Testing Strategy

- **Document status:** Draft v0.1
- **Date:** 2026-09-20
- **Release covered:** Release 1.0
- **Source:** user stories, Definition of Done, [SRS 5.1 evaluation cases](../requirements/SRS.md#51-evaluation-cases-minimum), [ADR-0002](../architecture/adr/0002-toolchain-baseline.md), build plan

## 1. Principles

- **Acceptance criteria drive tests.** Each Given/When/Then criterion in the user stories becomes at least one automated test, at the lowest level that proves it.
- **Test business rules, not implementation details.** Answer checking, mastery, limits, suspension rules, and authorization deserve thorough tests; framework wiring needs only enough to show it is connected.
- **No real external services in automated tests.** AI providers, photo storage, identity providers, and email are replaced by fakes or local stand-ins. Real providers are exercised only by the AI evaluation and by manual checks.
- **A test that cannot run is reported, not skipped silently.** If infrastructure is missing, say so and run what can run.

## 2. Test levels

| Level              | Tool                                             | Location                                                                                                                                   | What it covers                                                                                                                                                                                                                | Runs in                                                                                  |
| ------------------ | ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Web                | Vitest + Testing Library + jsdom; axe-core       | `apps/web/src/**/*.test.tsx`                                                                                                               | Components, forms, states (loading, error, empty), accessibility of labels and roles                                                                                                                                          | `pnpm test`, CI                                                                          |
| API unit           | xUnit v3; ArchUnitNET for the architecture rules | `apps/api-dotnet/LanguZe.Api.Tests/`                                                                                                       | Pure rules (answer normalization, mastery, day boundaries, suspension rules), Platform pieces in process, what the SDKs would send a provider, and the dependency rules of ADR-0006 section 1                                 | `dotnet test --project apps/api-dotnet/LanguZe.Api.Tests`, CI                            |
| API integration    | xUnit v3 + PostgreSQL                            | `apps/api-dotnet/LanguZe.Api.IntegrationTests/`                                                                                            | The API in process on a throwaway database, with fake providers and a clock the test moves: sessions that expire, provider sign-in with account linking, jobs, transactions under races; the EF Core migrations and `migrate` | `dotnet test --project apps/api-dotnet/LanguZe.Api.IntegrationTests`, CI with PostgreSQL |
| API contract       | xUnit v3 on .NET 10                              | `tests/api-contract/`; the API under test is started by `scripts/run-contract-tests.mjs`                                                   | The HTTP contract, black-box, against the API running as a process: status codes, bodies, error codes, cookies, headers, rate limits; then its OpenAPI document against `tests/api-contract/openapi.json`                     | `pnpm test:contract`, CI with PostgreSQL, Maildev and storage                            |
| Browser end-to-end | Playwright: Chromium, and WebKit as an iPhone    | `apps/web/e2e/*.spec.ts`; the API and web app are started by `apps/web/playwright.config.ts`                                               | The web app and the API together: sign-up and its email link, sign-in and out, a world, a game, the tutor, a reset ending sessions                                                                                            | `pnpm test:browser`, CI with PostgreSQL, Maildev and storage                             |
| Production image   | Docker + Node fetch                              | `scripts/web-image-check.mjs`                                                                                                              | The web app as Railway runs it: public/ files, static chunks, an optimized picture, the origin-secret 403 and 503                                                                                                             | `pnpm web:image-check`, CI job `image`                                                   |
| AI evaluation      | A separate command (increment 13)                | `apps/api-dotnet/LanguZe.Tools/Evaluation/`; photo and tutor cases in `apps/api-dotnet/evaluation/` ([AI evaluation](../ai/evaluation.md)) | The cases of SRS 5.1 against the real AI provider: safety check, extraction, tutor grounding and scope, prompt injection                                                                                                      | Manually before launch and before any model change; never in pull-request CI             |
| Load               | k6, outside pnpm (installed or its Docker image) | `tests/load/learners.js`; learners seeded by `LanguZe.Tools load-seed` ([performance review](performance-review.md))                       | NFR-002: 25 learners playing at once (V22), every non-AI endpoint p95 ≤ 500 ms                                                                                                                                                | Manually before a release that changes queries or endpoints; never in pull-request CI    |
| Manual             | Real browsers and phones                         | Checklists in the build plan increments; usability test; launch checklist        | Sign-in in Safari and the LINE and Facebook in-app browsers, photo capture on phones, usability testing with learners                                                                                                         | Increments 3 and 14                                                                      |

## 3. Conventions

- **Names describe behavior** in plain English, for example `refuses a request that changes data without an Origin header`, so a failing test reads as a broken rule.
- **Given/When/Then from the stories** maps to arrange, act, and assert inside the test.
- **Integration tests run the API as `Program.cs` builds it** (`ApiHost`, through `WebApplicationFactory`), so validation, errors, CORS, the `/v1` prefix and the session guard are the real ones; only the providers, the mail transport and the clock are replaced.
- **Probe routes** may be mapped in process to exercise shared behavior, such as request validation or rate limits, apart from any feature endpoint. They never ship.
- **Each integration test makes a database of its own** (`TemporaryDatabase`), migrated from empty and dropped at the end, so no test depends on another's data.
- **Contract tests know only HTTP.** They never reach into the API's code; the same suite judged the NestJS API and its ASP.NET Core port until the cutover (ADR-0006). What no endpoint can set up, such as an admin or a learner whose AI waits for review, a test writes with SQL into the database of the API under test (`tests/api-contract/Support/Database.cs`). The behavior under test is still reached only over HTTP. Each test stands for its own caller through `X-Forwarded-For`, which the API under test trusts for one hop, so per-address rate limits never mix two tests.
- **Fakes live next to the interface they replace**, for example the fake AI provider behind the AI interface (build plan decision B4), so every test and local run uses the same fake.
- **Time is injected** where a rule depends on it, such as Bangkok days, 24-hour sessions, and 7-day suspensions, so tests set the clock instead of waiting.

## 4. What must be covered

- Every acceptance criterion of the stories in the increment.
- Every error code an endpoint can return, with its HTTP status.
- Authorization: a learner can never read or change another learner's data (FR-008); admin endpoints refuse everyone else (FR-100).
- Invariants saved in one transaction: the attempt with its mastery and XP, the admin action with its audit entry, a daily limit with its new use.
- Privacy rules: logs and error responses never contain passwords, tokens, photos, prompts, tutor text, or query strings (NFR-008).
- Accessibility (NFR-012): a new learner page, or a new state a learner reaches by pressing something, gets an axe-core check with `expectNoAxeViolations` from `apps/web/src/test/axe.ts`; a new colour pair gets a line in `palette.test.ts`. See the [accessibility review](accessibility-review.md).

There is no numeric coverage target. A pull request is reviewed for whether its rules and error paths are tested, not for a percentage.

## 5. Continuous integration

Every pull request runs, as defined in `.github/workflows/ci.yml`:

1. Formatting, lint, and type checks.
2. The web app's unit tests, and the tests of the repository scripts (`pnpm test:scripts`), among them the OpenAPI comparison the contract tests rely on.
3. The web app's production build.
4. The .NET build with its unit and architecture tests, after a check that the C# is formatted; its integration tests on throwaway databases; then the API contract tests, with the API's OpenAPI document compared with `tests/api-contract/openapi.json`.
5. Browser end-to-end tests: the web app with the API, in Chromium and in WebKit as an iPhone, against a fresh database, Maildev, and photo storage. Provider sign-in, VoiceOver, and the LINE and Facebook in-app browsers stay manual.

Each job has a time limit, so a stuck run fails within minutes instead of the six hours GitHub allows (2026-11-06).

A pull request that changes documentation only, files under `docs/` and Markdown files anywhere, runs the first job, which covers the first three, and skips the rest: nothing they run reads documentation, and the formatting check still covers it. A single file of anything else runs them all. A push to `main` follows the same rule, comparing `main` before and after the push, so merging documentation does not rerun the heavy jobs; if the commit before cannot be fetched, everything runs (2026-11-09). Merging a pull request skips the heavy jobs too when it was already tested as it now stands: the merge commit holds exactly the pull request's last commit (the branch was up to date with `main`), nothing else was pushed to `main` in between, and CI passed on that commit as a pull request; otherwise everything runs (2026-11-16, to save GitHub Actions minutes). A pull request is merged only when every check that ran has passed.
