# #130 feat(api-dotnet): port the Gemini adapter for photo analysis

Merged 2026-10-31.

## Summary

Ports the Gemini adapter to ASP.NET Core (ADR-0006), the second half of the analysis slice: the safety check and the extraction run on Gemini once `GEMINI_API_KEY` is given, and the fake provider answers otherwise, as in NestJS.

- **`GeminiAiProvider`**, the only code that knows the SDK (`Google.GenAI` 1.27.0, as ADR-0006 names), behind `IGeminiModels` so tests stand in for the network. Same behaviour as `gemini-ai-provider.ts`: Gemini's own refusal is a block during the safety check and a provider error during extraction, an unknown category is `OTHER`, boxes go from Gemini's `[ymin, xmin, ymax, xmax]` of 1000 to LanguZe's fractions, a misshapen item is passed on for the rules to refuse, and a call past `AI_TIMEOUT_MS` is `TIMED_OUT`.
- **Prompts and schemas** word for word as `prompts.ts`, since the AI evaluation judged those words; a unit test pins each instruction's SHA-256.
- **Two C# SDK defaults differ from the JavaScript SDK's** (`@google/genai` 2.24.0, read in its source) and are set to match: one attempt with no retries, and no automatic continuation, which the JavaScript SDK does not have.
- **Settings**: `GEMINI_API_KEY`, required in production as in NestJS; `GEMINI_SAFETY_MODEL`, `GEMINI_EXTRACTION_MODEL`, `GEMINI_TUTOR_MODEL` (default `gemini-3.5-flash-lite`); `AI_TIMEOUT_MS` (1000 to 120000). The settings' text names the models, never the key. The module logs which provider answers, as NestJS does.
- `ApiHost` forces the fake provider whatever the machine's environment holds, as the contract runner already does. No test calls Gemini.
- The tutor's half comes with the tutor slice.

**New dependencies** (from `Google.GenAI`): `Google.Apis.Auth`, `Google.Apis`, `Google.Apis.Core` (with `Newtonsoft.Json` 13.0.3), `Microsoft.Extensions.AI.Abstractions`, `MimeTypes`, `System.Management`; `System.CodeDom` moves from 6.0.0 to 7.0.0 in the lock files. Google's libraries page still lists C# without a status, as ADR-0006 recorded (checked 2026-10-31).

## What the checks caught

- **The prompt comparison found a missing sentence.** Running `prompts.ts` under Node and reading the .NET prompts by reflection showed the extraction instruction without its first sentence, "You name objects in a photo for Thai adults learning English." Fixed, and the SHA-256 test now pins both instructions. The schemas match but for key order and `minItems`/`maxItems`, which the JavaScript SDK sends as the int64 strings `"4"` and the C# SDK as numbers.
- **CA1861** caught a constant array in a test.
- **A known difference, found by reading the code**: an answer that is JSON `null` makes NestJS throw a `TypeError` (`PROVIDER_ERROR`), while the .NET API reads it as no verdict (`INVALID_OUTPUT`). Both fail the analysis and give it back, and Gemini cannot answer `null` to an object schema; recorded in `COVERAGE.md`.
- Mutations, 10 of 10 caught: automatic continuation left on, Gemini's refusal ignored by the safety check, the box's x read from ymin, a timeout reported as a provider error, thoughts read as the answer, an unknown category kept as violence, a refusal during extraction ignored, a word of the safety instruction changed, the fake provider used despite a key (the first attempt did not compile; rewritten so it did, then caught), and usage reported as total tokens.

## Verification

| Check                                                | Result                           |
| ---------------------------------------------------- | -------------------------------- |
| `dotnet build` / `dotnet format --verify-no-changes` | 0 warnings, 0 errors / clean     |
| Unit / integration tests                             | 319/319 (27 new) / 94/94         |
| `pnpm test:contract --target dotnet`                 | 185/185, OpenAPI: No differences |
| `pnpm test:contract` (NestJS)                        | 185/185                          |
| `test:scripts` / `format:check` / `lint`             | 12/12 / pass / pass              |

No migration.

## Before merging

Staging's .NET service needs `GEMINI_API_KEY`, or it will not start: production requires it. A key of its own in the paid project, set and deployed on 2026-10-31 (production deployment, section 17).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
