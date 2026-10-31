# ADR-0004: Google Gemini as the first AI provider

- **Status:** Accepted
- **Date:** 2026-09-19; models chosen 2026-10-06 (see "Evaluation results")
- **Related:** AIR-001–AIR-009, FR-020–FR-025, FR-070–FR-076, FR-091–FR-094, NFR-001, NFR-003, NFR-017; PRD Q9; [architecture overview, section 5.1](../overview.md#51-ai-interface); [image to vocabulary flow](../../flows/image-to-vocabulary.md); [AI tutor flow](../../flows/ai-tutor.md)

## Context

LanguZe needs an AI provider for three jobs, each behind a LanguZe-owned interface (AIR-001):

1. **Safety check:** decide whether a photo breaks the Terms of Use, and in which category (FR-091, FR-092).
2. **Vocabulary extraction:** name 3–12 objects in a photo with an English word, a Thai meaning, an example sentence, a CEFR level, accepted variants, and a **highlight box** around each object (FR-022, AIR-003–AIR-005, AIR-009). The Identify game depends on the boxes being accurate.
3. **Tutor:** answer in Thai with English examples, reading the learner's data through read-only tools, and stream the reply (FR-070–FR-075, NFR-003).

Constraints: a budget of about USD 10 per month at launch (NFR-017); photos of learners' homes are personal data under Thailand's PDPA (AIR-006); Thai-language quality; and each provider's terms about who may use apps built on it.

## Options considered

| Provider               | For                                                                                                                                                                       | Against                                                                                                                                            |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Google Gemini**      | Documents object detection with bounding boxes; image input, structured output, tool calling, and streaming in one API; low-cost Flash-Lite models; available in Thailand | Its terms forbid apps directed at or likely to be used by people under 18; on the free tier, Google uses submitted content to improve its products |
| OpenAI                 | Allows users from 13, with parental permission and safeguards under 18                                                                                                    | Bounding boxes are not a documented feature; their accuracy would have to be proven first                                                          |
| Anthropic (Claude)     | Allows minors with safeguards; strong at following tutor instructions                                                                                                     | Bounding boxes are not a documented feature; their accuracy would have to be proven first                                                          |
| Self-hosted open model | Photos never leave LanguZe's infrastructure                                                                                                                               | GPU hosting costs far more than the budget, plus operations work for one developer                                                                 |

## Decision

1. **Provider:** the Gemini API, used through Google's official Gen AI SDK for JavaScript (`@google/genai`) inside the `ai` module's adapter only. No other module imports the SDK.
2. **Paid tier only for real learner data.** Production, and any environment that holds real learner data, uses a paid Gemini API key, because on the paid tier Google does not use prompts, images, or responses to improve its products. The free tier may be used in local development with test photos only.
3. **Adults only.** Gemini's terms exclude apps directed at or likely to be used by people under 18, so Release 1.0 is for adults (PRD Q9, V20).
4. **Models:** a current Gemini Flash-family model for each job, chosen as the cheapest model that passes the evaluation set (AIR-008, V8) and the PRD target of at least 80% correct words. Model IDs are configuration values, and changing one requires re-running the evaluation. At the time of writing, Flash-Lite models cost about USD 0.25–0.30 per million input tokens. The evaluation chose `gemini-3.5-flash-lite` for all three jobs (2026-10-06, below).
5. **Safety check as its own first call.** The photo is classified before extraction, with structured output: allowed, or blocked with an FR-091 category. This keeps the order that FR-092 requires, means a blocked photo never reaches extraction, and keeps each prompt to one job; the extra call costs a fraction of a US cent. If Gemini's own safety filters refuse the request or the response, that counts as a block, with the matching category or `OTHER`. An error or timeout is not a block: the analysis fails and is not counted (FR-025).
6. **Extraction output:** structured output with a JSON schema. Gemini returns boxes as `[ymin, xmin, ymax, xmax]` from 0 to 1000; the adapter converts them to the 0–1 values stored in the data model, and the `vocabulary` module applies the item rules (AIR-003, AIR-004). The image size sent to the model is chosen with the evaluation set, at most the prepared photo (V18). Boxes are relative, so resizing does not move them.
7. **Tutor:** tool calling with the three read-only tools of the [AI tutor flow](../../flows/ai-tutor.md#2-tools), streamed to the learner, with at most 5 tool rounds and an overall time limit of 30 seconds, both configurable (`AI_TUTOR_MAX_TOOL_ROUNDS`, `AI_TUTOR_TIMEOUT_MS`). The limit covers the whole reply rather than each call, so slow lookups cannot multiply the wait. The tutor's model is its own setting (`GEMINI_TUTOR_MODEL`). It first defaulted to a full Flash model, for fear that Flash-Lite would skip its tools and invent the learning history FR-075 forbids; the evaluation found Flash-Lite using its tools every time, and the tutor now shares the photo jobs' model. Exceeding either bound, or a reply with no text in it, fails the message, which is then released rather than counted (FR-071).
8. **Data sent:** photos only to the safety check and the extraction; for the tutor, only its instructions, recent messages, and tool results; never email addresses, display names, or photos (AIR-006). The Privacy Policy names Google as a processor of photos and tutor messages.
9. **Records and evaluation:** every call is recorded in `ai_calls` (AIR-007). The evaluation set runs as a separate command before launch and before any model change, not in pull-request CI ([AI evaluation](../../ai/evaluation.md)). A free-tier key may run it only while the set holds nothing but public photos and made-up learners, as it does.

## Evaluation results (2026-10-06)

Run by hand on a free-tier key against the [evaluation set](../../ai/evaluation.md): 44 photo cases and 13 tutor cases. The model behind `gemini-flash-lite-latest` at the time, `gemini-3.5-flash-lite`, was chosen for all three jobs and is now pinned as the default, by the rule above: the cheapest model that passes.

| Job          | Result                                                                                                                                | Target                          |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------- |
| Safety check | Every stand-in blocked in its category; no harmless photo blocked                                                                     | FR-091, FR-093                  |
| Extraction   | 98.8% of words correct (162 of 164, judged by a person); 5.9 words a world; safety check and extraction together p50 5.1 s, p95 6.9 s | ≥ 80% (PRD); NFR-001            |
| Tutor        | 13 of 13 cases, every one three times out of three; first piece of a reply p95 4.8 s                                                  | AIR-008, FR-072–FR-075; NFR-003 |

What the evaluation changed on the way:

- **A bug.** Every tutor reply that used a tool failed with HTTP 400: Gemini signs its function calls and refuses the next request without the signature. The adapter now sends Gemini's turn back as it came. No test could see it, since all of them use the fake provider.
- **The extraction prompt.** "Between 3 and 12 objects" gave 4.8 words a world; asking for every distinct object, up to 12, gave 5.9 with accuracy unchanged. Accepted answers must now name the same object, and the Thai meaning is the common dictionary one.
- **The tutor prompt.** "Politely decline" still wrote code in one off-topic reply of two; it now does no part of an off-topic request, and a case checks it still answers how to say a thing in English.
- **A rule.** An example sentence may use a regular plural of the word ("white tiles"); refusing it lost three words of eleven.
- **A policy.** Classical art is not nudity (SRS V21): the statue stand-in passed the check, and that was decided to be right.

The full Flash model (`gemini-flash-latest`) was tried for extraction. The few photos it finished gave more words, 6 to 8, but took longer than NFR-001 allows (p95 23.7 s), and most of the run failed on the free tier (HTTP 503 and 429), so it could not be judged fairly. It stays the next candidate if Flash-Lite falls short.

Known limitations, accepted for Release 1.0:

- Four sparse or repetitive photos give fewer than three words and fail with `TOO_FEW_WORDS`, which the learner is shown; the full Flash model found six words in one of them.
- A few accepted answers are still too loose, such as _heater_ for an air conditioner or _container_ for a vase.
- Nothing measures whether each highlight box marks one object; some cover two (#73).
- The same English word with a different Thai meaning becomes a second word for the learner (#72).
- `SEXUAL_CONTENT` has no stand-in, and `SUSPECTED_ILLEGAL_MATERIAL` never will; both rest on Gemini's own filters and FR-098.

## Tutor re-run (2026-10-23): plain-text replies

In production the tutor wrote Markdown, which the page shows exactly as written, so `**sofa**` reached the learner with its asterisks. The tutor prompt now asks for plain text, and the evaluation checks every reply for Markdown. The re-run used the same free-tier key and model:

- **13 of 13 cases** passed, each three times out of three, and none of the 39 replies used Markdown. Two cases hit the free tier's rate limit (HTTP 429) and were run again with `--pause 15`
- **The replies a person reads were read.** Refusals stay polite and offer English help instead, and no reply invents a learning history. The injected instruction in a wrong answer was quoted as what the learner typed, never followed
- **The first piece of a reply came at p95 17.4 s**, against 4.8 s on 2026-10-06 and NFR-003's 10 s. The prompt change adds no tool call; the slowest replies were the ones that look something up before answering. Whether production, on its paid key, meets NFR-003 is to be measured there

## Consequences

- LanguZe stays an adult product while Gemini is its provider. Serving teenagers later needs a provider whose terms allow minors, and that provider must pass the same evaluation set, including box accuracy. Thanks to the AI interface, that is a new adapter rather than a rewrite.
- A Google Cloud billing account is required. Production pays for Gemini with prepaid credit and auto-reload off, which caps the spending outright where a budget alert would only have warned ([production deployment, section 10](../../deployment/production.md#10-cost)). Rough cost per photo analysis (two calls) is well under one US cent, and per tutor message about a tenth of a cent; daily limits cap the total (NFR-017), and `ai_calls` shows the real figures.
- A safety check that blocks harmless photos would unfairly count toward suspensions (FR-093, V15). The evaluation set therefore includes everyday objects that can look risky, such as kitchen knives, medicine, and alcohol bottles, which must pass (SRS 5.1), and admins can lift wrongful suspensions (FR-105).
- A general AI safety check is not a specialized detector for child sexual abuse material. The legal handling of suspected illegal material stays on the pre-launch check list (SRS 3.10).
- Gemini model versions change often; IDs are pinned in configuration, and upgrades are deliberate, with an evaluation run.
