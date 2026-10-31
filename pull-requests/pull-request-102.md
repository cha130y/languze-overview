# #102 test(contract): add the first black-box contract tests and accept ADR-0006

Merged 2026-10-28.

## Summary

- **16 contract tests** in C# (xUnit v3, .NET 10) that judge the API by HTTP alone, so one suite holds NestJS and its ASP.NET Core port to the same standard. All pass against NestJS: health, security headers, the error format, the origin check, sign-up rules, sign-in and out, the emailed verification link, the seven-day session cookie (recognised by its attributes, not its name), and the sign-up limit
- **`pnpm test:contract`** (`scripts/run-contract-tests.mjs`) starts the API with the fake AI provider, Maildev and one trusted proxy hop, runs `dotnet test`, then stops it. Each test stands for its own caller through `X-Forwarded-For`, so per-address limits never set tests against each other
- **CI job `contract`**: locked-mode restore, `dotnet format --verify-no-changes`, the suite
- **ADR-0006 accepted**, as agreed once the first contract tests passed; docs index and overview updated
- Testing strategy (API contract level, a convention, CI item 5), local development guide, `CLAUDE.md`, and `/port-slice` describe the suite

## What the checks caught

- .NET 10's `dotnet test` no longer runs xunit.v3 through VSTest: `global.json` now selects Microsoft.Testing.Platform (and pins SDK 10.0.403), and `xunit.v3` is the only package
- The test platform sends usage data by default (Microsoft's docs list a three-octet IP among it); the script and CI opt out
- `.editorconfig` gave every file two spaces; C# now gets four
- FluentAssertions 8 is not under an open-source licence, so the tests use xUnit's `Assert`

## Verification

- `pnpm test:contract` locally: 16 of 16, twice
- `dotnet build` with warnings as errors: 0 warnings; `dotnet format --verify-no-changes`
- `pnpm format:check`, `pnpm test:scripts` (8 of 8)
- The `contract` CI job has not run yet; this PR is its first run

🤖 Generated with [Claude Code](https://claude.com/claude-code)
