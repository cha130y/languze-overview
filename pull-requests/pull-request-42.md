# #42 feat: the Gemini adapter

Merged 2026-09-24.

Increment 7 (#18), third of four pull requests. The pages are next.

## What it adds

The Gemini adapter (ADR-0004), which is **the only file that imports the SDK**: the analysis still knows nothing but LanguZe's own interface, so another provider would be another adapter rather than a change to the pipeline.

- **Structured output against a schema** for both jobs, with the prompts in their own file carrying a version, because what the model returns depends on their wording (AIR-008).
- **Boxes converted** from Gemini's `[ymin, xmin, ymax, xmax]` on 0–1000 into the 0–1 form the data model stores.
- **A misshapen item is passed on as it is.** The vocabulary rules are the one place that decides what may be saved, so this file changes shape and never judgement (AIR-003).
- **Nothing about the learner is sent**: the prepared photo and fixed instructions only, and text inside a photo is named as part of the picture rather than as something to obey (AIR-002, AIR-006).

**The distinction worth reading closely:** Gemini's own filters refusing the **safety check** counts as a block with the matching category — a photo its filters will not look at is not one to keep. Refusing the **extraction** is a provider error, because that photo already passed the check. The difference decides whether the learner loses one of their ten analyses (FR-025, FR-093).

Switching it on is one setting: with `GEMINI_API_KEY` the module builds this adapter, without it the fake answers, and the API logs which at startup.

## Two guards

- **The e2e suite pins `GEMINI_API_KEY` to empty**, as it already does for the web origin and photo storage. Otherwise a developer with a key in `.env` would send every test photo to a paid API.
- **The new install scripts are listed** in `pnpm-workspace.yaml`, both disabled: the SDK's only prints a line and protobufjs's builds a CLI nothing here uses.

## Verified

`lint`, `typecheck`, `build` and `format:check` pass; **183 API** unit tests (18 new) and 58 web, 119 e2e unchanged.

The adapter is driven through a stand-in for the SDK: verdicts both ways, an unknown category falling back to `OTHER`, both refusal paths, box conversion checked by arithmetic, a broken item passed on and then rejected by the real rules, timeouts, non-JSON answers, and a check that the request carries the image but nothing learner-shaped.

**Not verified: any real call to Gemini.** That needs a key and costs money, so none was made. Two things therefore remain unproven until someone runs it with a key: that the model honours these schemas as written, and that the box convention matches what it returns. The evaluation set in increment 13 is where that gets settled properly
