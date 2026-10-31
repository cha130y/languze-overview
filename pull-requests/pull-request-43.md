# #43 feat: the analysis pages

Merged 2026-09-25.

Increment 7 (#18), last of four pull requests. This finishes the increment.

## What a learner sees

A world's page now shows whichever of the three things is true of it, and gets there on its own.

- **While it runs** — the page asks the API for the status every three seconds and shows the result when it arrives (P3). It says the analysis is happening on the server and that leaving the page is fine, because someone who believes they have to watch a spinner for 20 seconds will watch it.
- **When it fails** — the reason, what to do about it, and **whether the attempt used one of today's ten**. A failure of ours does not; a blocked photo does (FR-025, FR-093). Saying which is what stops a learner from being afraid to press retry.
- **When it is ready** — the words beside their boxes on the photo. Each box carries the same number as its row, so which word belongs to which object never depends on colour (NFR-012), and pointing at a row lights up its box.

**Removing a word** asks first, and the last word offers no button at all: a world with nothing to practise is not a world, so the learner deletes it instead (V5).

**Today's count** is on both the list and the create page, with the reset written in Bangkok time (V1). With none left, the form is not offered at all — nobody should upload a photo over a phone connection only to be turned away once it arrives. A count the API could not give is treated as unknown rather than as zero.

**Two rules said on screen as well as enforced by the API.** Retry is hidden when the photo is gone, and the remove button is hidden on the last word. The API still decides in both cases and its refusal is still shown; the page only avoids offering a button that could never work.

## Also here, and worth your judgement

`AI_FAKE_DELAY_MS` on the fake provider. It answers instantly, so the waiting page was **unobservable** — I could not see it and neither could you. Setting it to 8000 makes an analysis take about sixteen seconds. Default 0, and pinned to 0 in the e2e config for the same reason the Gemini key is. This is the one thing here you did not ask for; say if you would rather it went.

## Verified in a real browser

Headless Chrome, three runs against the fake provider. The startup log confirmed the fake provider each time — **no Gemini call, nothing spent**.

|                            |                                                            |
| -------------------------- | ---------------------------------------------------------- |
| Waiting → ready, untouched | after 25s; status asked every ~3s                          |
| Box positions              | API `sofa@0.05,0.45` drawn at `5%,45%`, all six            |
| Removing a word            | asks first, 6 → 5 words, list card follows                 |
| Daily count                | 10/10 → 9/10 after one analysis                            |
| Failure and retry          | shown on its own, retry re-runs it, **count stayed 10/10** |
| Blocked photo              | no retry offered, photo gone, count 10/10 → 9/10           |

`lint`, `typecheck`, `format:check` and `build` pass. **185 API** unit tests (2 new), **93 web** (35 new), **119 e2e** unchanged. Four mutations — the last-word rule, the box arithmetic, the refresh trigger, the limit gate — each broke exactly the test meant to catch it.

**Not verified:** these pages on a real phone, which is still the carried-over NFR-018 check. And no real Gemini output has ever driven these boxes, so whether real coordinates land on real objects remains unproven until someone runs it with a key.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
