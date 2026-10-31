# LanguZe — Testing Strategy

- **Document status:** Draft v0.1
- **Date:** 2026-09-20
- **Release covered:** Release 1.0
- **Source:** [user stories, Definition of Done](../requirements/user-stories.md#2-definition-of-done), [SRS 5.1 evaluation cases](../requirements/SRS.md#51-evaluation-cases-minimum), [ADR-0002](../architecture/adr/0002-toolchain-baseline.md), [build plan](../planning/build-plan.md)

## 1. Principles

- **Acceptance criteria drive tests.** Each Given/When/Then criterion in the user stories becomes at least one automated test, at the lowest level that proves it.
- **Test business rules, not implementation details.** Answer checking, mastery, limits, suspension rules, and authorization deserve thorough tests; framework wiring needs only enough to show it is connected.
- **No real external services in automated tests.** AI providers, photo storage, identity providers, and email are replaced by fakes or local stand-ins. Real providers are exercised only by the AI evaluation and by manual checks.
- **A test that cannot run is reported, not skipped silently.** If infrastructure is missing, say so and run what can run.

## 2. Test levels

| Level            | Tool                                             | Location                                                                                                                            | What it covers                                                                                                                         | Runs in                                                                                  |
| ---------------- | ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| API unit         | Vitest                                           | `apps/api/src/**/*.spec.ts`, next to the code                                                                                       | Pure rules (answer normalization, mastery, day boundaries, suspension rules), services with their collaborators faked, guards, filters | `pnpm test`, CI                                                                          |
| API end-to-end   | Vitest + Supertest + PostgreSQL                  | `apps/api/test/*.e2e-spec.ts`                                                                                                       | Every endpoint: success, main error codes, authorization, transactions; the whole application with `configureApp`, as in production    | `pnpm test:e2e`, CI with a PostgreSQL service                                            |
| Web              | Vitest + Testing Library + jsdom; axe-core       | `apps/web/src/**/*.test.tsx`                                                                                                        | Components, forms, states (loading, error, empty), accessibility of labels and roles                                                   | `pnpm test`, CI                                                                          |
| .NET unit        | xUnit v3; ArchUnitNET for the architecture rules | `apps/api-dotnet/LanguZe.Api.Tests/`                                                                                                | The port's pure rules, Platform pieces in process, and the dependency rules of ADR-0006 section 1                                      | `dotnet test --project apps/api-dotnet/LanguZe.Api.Tests`, CI                            |
| .NET integration | xUnit v3 + PostgreSQL                            | `apps/api-dotnet/LanguZe.Api.IntegrationTests/`                                                                                     | The EF Core migrations on throwaway databases: the cutover migration on accounts in Better Auth's shape, and back                      | `dotnet test --project apps/api-dotnet/LanguZe.Api.IntegrationTests`, CI with PostgreSQL |
| API contract     | xUnit v3 on .NET 10                              | `tests/api-contract/`; the API under test is started by `scripts/run-contract-tests.mjs`                                            | The HTTP contract both API implementations keep (ADR-0006): status codes, bodies, error codes, cookies, headers, rate limits           | `pnpm test:contract`, CI with PostgreSQL, Maildev and storage                            |
| Browser          | Playwright: Chromium, and WebKit as an iPhone    | `apps/web/e2e/*.spec.ts`; the API and web app are started by `apps/web/playwright.config.ts`                                        | The web app and the API together: sign-up and its email link, sign-in and out, a world, a game, the tutor, a reset ending sessions     | `pnpm test:browser`, CI with PostgreSQL, Maildev and storage                             |
| AI evaluation    | A separate command (increment 13)                | `apps/api/src/evaluation/`; photo and tutor cases in `apps/api/evaluation/` ([AI evaluation](../ai/evaluation.md))                  | The cases of SRS 5.1 against the real AI provider: safety check, extraction, tutor grounding and scope, prompt injection               | Manually before launch and before any model change; never in pull-request CI             |
| Load             | k6, outside pnpm (installed or its Docker image) | `apps/api/load/learners.js`; learners seeded by `load:seed` ([performance review](performance-review.md))                           | NFR-002: 25 learners playing at once (V22), every non-AI endpoint p95 ≤ 500 ms                                                         | Manually before a release that changes queries or endpoints; never in pull-request CI    |
| Manual           | Real browsers and phones                         | Checklists in the build plan increments; [usability test](usability-test.md); [launch checklist](../deployment/launch-checklist.md) | Sign-in in Safari and the LINE and Facebook in-app browsers, photo capture on phones, usability testing with learners                  | Increments 3 and 14                                                                      |

## 3. Conventions

- **Names describe behavior** in plain English, for example `refuses a request that changes data without an Origin header`, so a failing test reads as a broken rule.
- **Given/When/Then from the stories** maps to arrange, act, and assert inside the test.
- **End-to-end tests build the application the way `main.ts` does**, through `configureApp`, so validation, errors, CORS, the `/v1` prefix, and guards are the real ones.
- **Test-only controllers** may be registered in end-to-end tests to exercise shared behavior before feature endpoints exist, as `test/platform.e2e-spec.ts` does. They never ship.
- **Each end-to-end test file creates the data it needs** and does not depend on data from other files. Once tables exist (increment 2), a helper resets the tables a file uses before it runs.
- **Contract tests know only HTTP.** They never reach into the API's code or database, so the same suite judges the NestJS API and its ASP.NET Core port. Each test stands for its own caller through `X-Forwarded-For`, which the API under test trusts for one hop, so per-address rate limits never mix two tests.
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
2. API and web unit tests, and the tests of the repository scripts (`pnpm test:scripts`), among them the OpenAPI comparison the port to ASP.NET Core relies on (ADR-0006).
3. Production builds.
4. API end-to-end tests against a fresh PostgreSQL database, after `prisma migrate deploy`.
5. The .NET build with its unit and architecture tests, after a check that the C# is formatted; its integration tests, the migrations on throwaway databases; the API contract tests against the NestJS API; then the same tests against the ASP.NET Core API for the slices ported so far, with its OpenAPI document compared with NestJS's on their paths.
6. Browser smoke tests: the web app with the API, in Chromium and in WebKit as an iPhone, against a fresh database, Maildev, and photo storage. Provider sign-in, VoiceOver, and the LINE and Facebook in-app browsers stay manual.

A pull request is merged only when all of them pass.
