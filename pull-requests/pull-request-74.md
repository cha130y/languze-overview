# #74 feat(api): choose the AI models with the evaluation

Merged 2026-10-06.

## What

Increment 13, part 3 of 3: the AI evaluation run against Gemini, and the models chosen by it. Closes #24.

**Chosen: `gemini-3.5-flash-lite` for all three jobs**, now the pinned default (ADR-0004: the cheapest model that passes).

| Job          | Result                                                                          | Target               |
| ------------ | ------------------------------------------------------------------------------- | -------------------- |
| Safety check | Every stand-in blocked in its category; no harmless photo blocked               | FR-091, FR-093       |
| Extraction   | 98.8% correct (162/164 words, judged by a person); 5.9 words a world; p95 6.9 s | ≥ 80% (PRD); NFR-001 |
| Tutor        | 13/13 cases, each 3 times out of 3; first piece p95 4.8 s                       | AIR-008; NFR-003     |

## Commits

1. **Evaluation fit for a real run**: `--pause` for free-tier limits, HTTP status on failures, dropped words and accepted answers in the report, one box at a time, `NOT_BLOCKED` for must-pass safety cases, tutor language bounds measured from 66 replies, the synthetic note kept out of scores. Complete photo set with sources and 164 judged words
2. **Classical art is not nudity** (SRS V21): the statue stand-in passed, decided right; Terms of Use updated with their own date. Safety prompt unchanged on purpose
3. **Prompts and a rule**: extraction asks for every distinct object (4.8 → 5.9 words a world), same-object accepted answers only, dictionary Thai meanings; tutor does no part of off-topic work (wrote Python before); example sentences may use regular plurals
4. **Pin and record**: model defaults, ADR-0004 results and limitations, deployment docs

## Known limitations (ADR-0004)

- 4 sparse photos give fewer than 3 words (`TOO_FEW_WORDS`)
- A few accepted answers still loose (heater for an air conditioner)
- Box quality unmeasured (#73); duplicate words when the Thai meaning differs (#72)

## Before deploying

- Railway: set `GEMINI_*_MODEL` to `gemini-3.5-flash-lite` or remove them; production needs a **paid** key
- `.env.example`, if it lists those models

## Verification

- lint (zero warnings), typecheck, format:check, build pass
- API 447 tests, web 268; e2e 288/288 (second run; the first hit the known Windows worker crash)
- Mutation checks on each new piece of logic, all caught
- Pinned model name checked against Gemini for all three jobs

🤖 Generated with [Claude Code](https://claude.com/claude-code)
