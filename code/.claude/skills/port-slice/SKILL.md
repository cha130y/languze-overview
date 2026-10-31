---
name: port-slice
description: Ports one slice of the LanguZe API from NestJS to ASP.NET Core (ADR-0006) — contract tests first, then the .NET implementation, checked against both APIs and the OpenAPI comparison.
argument-hint: <module-or-endpoints>
---

# Port One Slice to ASP.NET Core

The NestJS API is the specification. The goal is the same behavior in .NET, proven by checks, not an improved version.

If `tests/api-contract` or `apps/api-dotnet` does not exist yet, stop: the contract-test and skeleton pull requests come first (ADR-0006, section 2).

1. **Name the slice.** List the NestJS module, its endpoints (from `apps/api/openapi.json`), the requirements they serve (SRS IDs, flows, ADR rules), the end-to-end tests in `apps/api/test` that pin their behavior, and their rows in `tests/api-contract/COVERAGE.md`. State this before editing.
2. **Branch** from an up-to-date `main` after checking `git status --short`.
3. **Contract tests first**, in `tests/api-contract`: every endpoint, status code, error code, and rule of the slice, written from the requirements and the NestJS end-to-end tests, never from the new code. Each test class carries the slice's `[Trait("Slice", "<Slice>")]`, named as in `COVERAGE.md`. For each `it(...)` of those e2e files, name the contract test that covers it in `COVERAGE.md`, or say why none can. Run them against NestJS with `pnpm test:contract`. They must pass there before any .NET code is written. A test that fails against NestJS is either wrong or has found a NestJS bug; report a bug to the user rather than fixing it inside the port, and record an accepted difference under "Known differences" in `COVERAGE.md`.
4. **Implement** in `apps/api-dotnet/LanguZe.Api`, in the structure of ADR-0006 section 1: one `Features/<Slice>/` per endpoint (endpoint, request and response records, handler), pure rules in the module's `Domain/`, what slices share in `Shared/`, background work in `Jobs/`, what other modules may call in `Contracts/`, entities in `Data/Entities`. Response records match NestJS field for field. Go through the traps below.
   - **A table the slice reads for the first time** is adopted, not created: add its entity to `Data/Entities`, matched column for column to the table in Prisma's migration SQL, then `dotnet ef migrations add Adopt<Tables> --project apps/api-dotnet/LanguZe.Api --output-dir Data/Migrations`. Read the generated `Up` against Prisma's SQL: the names, types and indexes must match, which is the check that the entity is right. Then empty `Up` and `Down`, as `Baseline` is, since Prisma has created the table already (ADR-0006, section 4). `MigrationTests` fails if the migration is missing. `pnpm db:dotnet` applies it locally, and staging takes it as production deployment, section 17, says
   - A real schema change before cutover is a decision to raise first: production's schema still belongs to Prisma.
   - **A request body** is a record the endpoint takes as `Body<T>` (`Platform/Validation`), each property carrying the rules of its NestJS DTO field as attributes named after the decorators, written in the same order: `@IsEmail() @MaxLength(254)` becomes `[property: IsEmail, MaxLength(254)]`, and `@Type(() => Number)` becomes `ToNumber`. A decorator with no attribute yet gets one, checked against what NestJS answers, never against what seems right. Never DataAnnotations: they run after binding, so they answer a value of the wrong type differently (ADR-0006, section 1). Pin the slice's `VALIDATION_FAILED` answers field by field in its contract tests, as `RequestValidationTests` does.
   - **Who may call it.** Every route needs a session unless its endpoint says otherwise, as NestJS's global guard does: `@Public()` becomes `.AllowAnonymous()` and `@AllowsPendingSignUp()` becomes `.AllowPendingSignUp()`. Its limits come along too: `@Throttle(...)` becomes `.WithRateLimit(limit, window)` and `@SkipThrottle()` becomes `.SkipRateLimit()`. The signed-in account comes from `CurrentAccount` (Auth's `Contracts`), never from the request.
5. **Check:**
   - add the slice to `PORTED_TO_DOTNET` in `scripts/run-contract-tests.mjs`, with its path prefixes, or with its operations, such as `GET /v1/me`, when the slice is ported in parts. The tests of a part still to come carry a trait of their own until then, as the email tests did under `AuthEmail` while Auth was ported in parts; then `pnpm test:contract --target dotnet` runs the contract tests of every ported slice against the .NET API and compares its OpenAPI document with `apps/api/openapi.json` on those paths, and both must pass. Run `pnpm test:contract` against NestJS too
   - `dotnet build LanguZe.slnx`, `dotnet test --project apps/api-dotnet/LanguZe.Api.Tests` (the architecture rules and `MigrationTests` run here) and `dotnet format LanguZe.slnx --verify-no-changes`
   - `dotnet test --project apps/api-dotnet/LanguZe.Api.IntegrationTests` when the slice adds a migration, or behavior no black-box test can reach yet: a state only a later slice can create, such as a suspended account, or one that takes days, such as a session expiring. Those tests run the API in process with a clock they move (`ApiHost`), and `COVERAGE.md` lists them under "Covered another way"
   - `pnpm test:browser` with the web app pointed at the .NET API, when the slice touches sign-in, cookies, or a flow those tests walk through
   - the slice's rows in `COVERAGE.md` are complete
6. **Review the diff:** nothing changed in `apps/api` except a reported bug fix, no secrets, no generated files, no unrelated edits.
7. **Documents:** behavior should not have changed. If it had to, say why and update the requirement or flow it touches.
8. **Report** what was ported, the contract tests added, the OpenAPI comparison result, and **what the checks caught** on the way — a failing contract test, a difference the comparison found, an architecture rule that broke. The pull request carries this list.

## Traps the checks may not catch

Found by reviewing the NestJS code before the port; each has bitten ports like this one.

- **JavaScript and C# differ quietly.** `toLowerCase()` is `ToLowerInvariant()`, never `ToLower()`. In a regular expression, `\s` and `\d` do not match the same characters in both (.NET's `\d` takes every script's digits), and .NET's `$` also matches before a final newline: spell the characters out and end with `\z`, as `ValidatorJs` does. A string length counts UTF-16 units in both, but validator.js's `isLength` counts an emoji once. `localeCompare` sorts by ICU collation, not ordinally. A JavaScript `Date` is always UTC: use `DateTimeOffset` or UTC `DateTime` for `timestamptz`, and `DateOnly` where NestJS sends a day as text (V1). Bangkok days use the fixed +07:00 offset.
- **A library's own checks.** Behind the DTO, a library NestJS calls may refuse what the DTO let through. Better Auth checks an address again with zod, which refuses some that validator.js accepts, and measures a password in UTF-16 units, where validator.js counts an emoji once (`BetterAuthRules`). Read the library's code for what it checks and answers, and pin each case with a contract test.
- **What a library does to hide who has an account.** Better Auth answers an unknown address no sooner than a known one: asking for a verification email takes half a second at least. Read for such floors and pin them; warm the API up with a first request, which alone can take as long, or the test passes without the floor.
- **Text a learner reads.** Copy the Thai of an email or a message from the NestJS source with a script rather than retyping it, and pin it word for word in a contract test, as `AuthVerificationTests` pins the verification email; running it against NestJS proves the copy.
- **JSON as NestJS writes it.** Instants as `2026-10-28T09:14:38.397Z` (`UtcInstantConverter`), enums as their string names, `null` written rather than left out. The OpenAPI comparison ignores formats, so a contract test has to pin them. In the document, a whole number is a `number` and an instant a `string`, as NestJS writes them; `JsonTypeSchemaTransformer` sees to both, and `ApiProperty` gives a schema the limits and optional fields NestJS documents.
- **`details.fields` has two shapes.** DTO validation gives an array of `{ field, rules }`; a service refusing a field itself gives an object of field to message (moderation's `endsAt`). Keep the one the NestJS code path gives.
- **Races are part of the contract.** A unique-violation catch makes answering twice return the recorded result; `SELECT … FOR UPDATE` serializes admin actions and account deletion; `INSERT … ON CONFLICT DO UPDATE` counts tutor messages atomically. Port that SQL as it is, and test it with parallel requests.
- **The database keeps rules of its own.** Eight CHECK constraints, PostgreSQL enum types, a `jsonb` and a `text[]` column, and triggers that make `admin_actions` append-only: never update or delete an audit row.
- **Writes across owners** are only the ones in the data model, section 4.9. A new one is a decision to explain.
- **Jobs keep their cost behavior.** A job that reads the database every minute keeps Neon awake; port the gating that skips the read while nothing can be waiting.
- **Photos keep their privacy.** The stored photo has no EXIF left, GPS included (FR-017).
- **Provider sign-in keeps its addresses.** The callback paths registered with Google, LINE and Facebook stay as they are.

Do not commit or push automatically. Use `/commit` only after the user explicitly asks to ship the change.
