# #71 fix(api): send Gemini's turn back whole after a tool call

Merged 2026-10-06.

## What

The tutor could not answer anything that needed a tool. Against the real Gemini, every such reply failed with HTTP 400: "Function call is missing a thought_signature in functionCall parts". Found by the AI evaluation (#24).

Gemini signs each function call it makes and refuses the next request unless its turn comes back as it was sent. The adapter rebuilt that turn from the text and the calls only, dropping the signature. In production, any question about a learner's own words or mistakes (FR-072, FR-075) would have failed.

## Change

- The adapter keeps the parts Gemini streamed, signatures included, and sends them back as its turn
- Text and function calls are read from those parts rather than the SDK getters, which also ends the warning the SDK logged on every tool round
- A part marked as the model's own thought is never shown to the learner
- `docs/flows/ai-tutor.md` notes the rule

## Why tests missed it

Every test uses the fake provider, and the adapter's tests faked chunks with only `text` and `functionCalls`, a shape no real response has. They now stream parts under the response's candidate, as the SDK does.

## Verification

- On this commit alone: API lint (zero warnings), typecheck, format:check, build pass; API 427 tests (+2)
- Mutation: both new tests fail against the old code (turn rebuilt without the signature; thoughts shown)
- Real Gemini (`gemini-flash-lite-latest`): the question that failed with 400 is answered from the tool's data. The full tutor evaluation went from 3/12 to 11/12 cases
- e2e not run: the API e2e tests use the fake provider, which this does not touch

🤖 Generated with [Claude Code](https://claude.com/claude-code)
