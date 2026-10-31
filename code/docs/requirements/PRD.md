# LanguZe — Product Requirements Document (PRD)

- **Document status:** Draft v0.10 — decisions recorded (section 12); detailed rules in the [SRS](SRS.md)
- **Date:** 2026-09-19
- **Product release covered:** Release 1.0 (first production release; opened to the public in a later step, Q10)
- **Next documents:** SRS (`FR-xxx`) → user stories (`US-xxx`) → use cases (`UC-xxx`) → process flows

## 1. Product overview

**LanguZe** (pronounced _แลน-กุ-เซ่_) — **Learn from your world.**

_เปลี่ยนสิ่งรอบตัวคุณให้กลายเป็นบทเรียนภาษา_

LanguZe is an AI-powered, gamified vocabulary-learning web application. A learner photographs a real place from their own life, such as their room, kitchen, or workplace. AI turns the objects in that photo into English vocabulary with Thai meanings, the learner practises those words through a game, LanguZe remembers which words the learner gets wrong, and a personal AI tutor uses that history to explain and coach.

## 2. Problem statement

Thai speakers see and use everyday objects all day, but typical vocabulary apps teach from fixed, generic word lists:

- **Disconnected content.** Lessons such as "Animals" or "Food" are chosen by the app, not by what the learner actually sees and needs.
- **Right/wrong without understanding.** Most exercises mark an answer correct or incorrect without explaining the mistake in the learner's own language.
- **No memory of personal weak points.** Progress is tracked as lessons completed or points earned, not as which specific words a learner keeps getting wrong.

As a result, learners forget words that never connected to their real environment and keep repeating the same mistakes.

## 3. Product vision

Turn the learner's own world into personalized language lessons, with AI as the learning engine rather than an add-on feature.

| Pillar                    | What it means for the learner                                                    |
| ------------------------- | -------------------------------------------------------------------------------- |
| **Learn from your world** | Upload a photo of a real place and receive vocabulary about the things in it.    |
| **Adaptive learning**     | LanguZe remembers each word's mastery and brings weak words back for practice.   |
| **AI personal tutor**     | Ask questions and get explanations grounded in your own vocabulary and mistakes. |
| **Gamification**          | XP and progress make practice engaging; more game types and multiplayer follow.  |

Positioning compared with traditional vocabulary apps:

| Traditional app      | LanguZe                                     |
| -------------------- | ------------------------------------------- |
| Predefined lessons   | Lessons generated from the learner's photos |
| Generic exercises    | Practice driven by the learner's mistakes   |
| Correct / wrong only | AI explains mistakes                        |
| Course-centric       | Learner-centric: "learn from your world"    |

## 4. Target users

### Primary persona: Thai-speaking English learner

- Native Thai speaker who wants practical English vocabulary for everyday surroundings.
- An adult aged 18 or older, such as a university student or a working adult. Release 1.0 is for adults only (Q9).
- English level from CEFR A1 to B1.
- Uses a smartphone or laptop browser and can take or upload photos; commonly uses LINE and often opens links inside the LINE app.
- Prefers explanations in Thai with English examples.

### Future users (later releases)

- English speakers learning Thai, and learners of other language pairs.
- Teachers who prepare worlds or track learners.
- Teenage learners aged 13–17, with parental consent (see roadmap).

## 5. Release 1.0 features

| #   | Feature                  | Description                                                                                                                                                                                                                                                                       |
| --- | ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| F1  | Account                  | Sign up and sign in with email and password, Google, LINE, or Facebook; email verification; password reset; account deletion. AI features require a verified account.                                                                                                             |
| F2  | My World                 | Create a named world (for example "My Room") by uploading a photo; list, open, rename, and delete the learner's worlds.                                                                                                                                                           |
| F3  | AI vocabulary extraction | AI identifies 3–12 objects in the photo and produces, per object, an English word with its location in the photo, a Thai meaning, an example sentence, and a CEFR level. Output is validated before it is saved; learners can remove wrong words.                                 |
| F4  | Identify game            | The learner sees their own photo with one object highlighted and types the English word, or chooses "I don't know" to see the answer. Answers are checked against accepted variants; feedback shows the correct word, its Thai meaning, and the example sentence.                 |
| F5  | Mistakes and mastery     | Every answer updates the learner's mastery of that word: `NEW` → `LEARNING` → `FAMILIAR` → `MASTERED`. Mastery is shared across all worlds that contain the word. Incorrect answers are recorded as mistakes.                                                                     |
| F6  | Personalized review      | A review session that prioritizes the learner's most recent mistakes and least recently practised words across all worlds.                                                                                                                                                        |
| F7  | Progress and XP          | Correct answers earn XP; the learner sees total XP and word counts per mastery state.                                                                                                                                                                                             |
| F8  | AI tutor                 | A chat where the learner asks about their vocabulary (for example "Why do I keep getting this word wrong?"). The tutor retrieves the learner's weak words and recent mistakes through application tools and explains in Thai with English examples.                               |
| F9  | Safety and policies      | Terms of Use and Privacy Policy accepted at sign-up; an automatic safety check of every photo; vocabulary covers objects and places only, never people; repeated rule-breaking suspends AI features; a moderation admin area where admins review suspensions and act on accounts. |

Exact rules (accepted answer variants, mastery transitions, XP amounts, session composition, limits) are defined in the [SRS](SRS.md).

## 6. User journey

```text
Sign up (accept Terms of Use) / sign in → verify account
      ↓
Create a world: name it and upload a photo      (e.g., "My Room")
      ↓
AI analyses the photo → 3–12 words with location, Thai meaning, example sentence, CEFR level
      ↓
Review the word list and remove any wrong words
      ↓
Play Identify: see an object highlighted in the photo → type the English word
      ↓
Feedback → correct: XP · incorrect: correct word + meaning + example
      ↓
Mastery updated, mistakes recorded
      ↓
Personalized review of weak words
      ↓
Ask the AI tutor about words and mistakes
```

## 7. Release 1.0 scope

**In scope:** features F1–F9 as a responsive web application for mobile and desktop browsers, deployed to production.

**Release criteria:**

- The operator can complete the full journey in section 6 in production.
- AI extraction, answer feedback, and tutor behaviors have evaluation cases (expected, edge, and failure) that pass.
- CI passes on `main` (format, lint, typecheck, tests, build, API e2e).

The first criterion asked for a new learner completing the journey without assistance, judged in a closed usability test (section 9). That test moved to before the public opening on 2026-10-26: until then the operator alone uses production, and the test is most useful on the system that opens.

**Release steps (Q10):** Release 1.0 is released once these criteria are met, and is then used in production by the operator. Opening it to the public is a later step, taken once invited testers have passed the usability test and the legal and privacy work of the [launch checklist](../deployment/launch-checklist.md#7-before-the-public-opening) is done. The features and the open sign-up of Q6 do not change between the two steps; only who can reach production does.

## 8. Roadmap (later releases)

Candidates, in no fixed order; each needs its own requirement and, where architecture changes, an ADR:

- Additional game types: multiple choice, spelling, translation, sentence writing with AI feedback, listening.
- AI-generated practice scenes when the learner has no suitable photo.
- AI tutor grounded in full learning history through retrieval-augmented generation (RAG) with PostgreSQL + pgvector.
- Daily Challenge built from weak and almost-mastered words.
- Real-time multiplayer challenges and leaderboards.
- Achievements and notifications, including weekly learning reports by email.
- Passkeys and multi-factor authentication.
- English → Thai direction and other language pairs.
- Teacher role and community features.
- Teenage learners (13–17): parental consent, tutor rules suited to minors, and an AI provider whose terms allow minors.
- Exam vocabulary tracks (for example TOEIC, TOEFL, and IELTS): curated word lists that link to the same vocabulary words and mastery, practised with text-based game types. Words from the learner's photos can be marked when they appear on an exam list. Test names are trademarks of their owners; official test questions and copyrighted official word lists are not reproduced.

## 9. Success metrics

Targets marked _proposed_ are starting values to confirm or adjust.

| Area                | Metric                                                                                                                     | Target                                      |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------- |
| Core journey        | Participants in usability testing who complete sign-up → world → game → review → tutor without help                        | ≥ 5 participants (_proposed_)               |
| AI extraction       | Share of extracted words that correctly name an object visible in the photo, on an evaluation set of everyday-scene photos | ≥ 80% (_proposed_)                          |
| AI output safety    | AI results persisted without passing schema validation                                                                     | 0 (invariant)                               |
| Responsiveness      | Photo upload to vocabulary ready (95th percentile)                                                                         | ≤ 20 seconds (_proposed_)                   |
| Engineering quality | CI status on `main`; evaluation cases for AI behaviors                                                                     | Always green; cases for F3, F4 feedback, F8 |
| Operating cost      | Monthly infrastructure and AI spend at launch                                                                              | ≤ USD 10 (initial budget)                   |

## 10. Non-goals for Release 1.0

- Native iOS or Android apps.
- Payments, subscriptions, or ads.
- Game types other than Identify.
- Real-time multiplayer, leaderboards, and achievements.
- AI image generation.
- RAG and long-term learning-history search.
- Speech, pronunciation, and listening practice.
- Languages or directions other than Thai speakers learning English.
- Teacher, organization, or admin dashboards beyond the moderation admin area in F9.
- Offline use.

## 11. Constraints, assumptions, and risks

**Constraints**

- Developed and operated by a single developer; each technology must be justified by a feature ([ADR-0001](../architecture/adr/0001-modular-monolith.md), [ADR-0002](../architecture/adr/0002-toolchain-baseline.md)).
- Initial operating budget of about USD 0–10 per month, using free tiers where possible.

**Assumptions**

- Learners are comfortable uploading photos of their surroundings.
- A vision-capable AI model on a free or low-cost tier is accurate enough to name and locate everyday objects.

**Risks**

| Risk                                                              | Mitigation direction                                                                                                                                                            |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AI misidentifies objects or invents words                         | Treat AI output as untrusted: schema validation, deterministic post-processing, evaluation set; learners can remove wrong words.                                                |
| Photos contain personal information (faces, documents, addresses) | Send only what the feature needs to AI providers; learners can delete worlds and their account.                                                                                 |
| Text inside a photo tries to instruct the AI (prompt injection)   | Constrain prompts and structured outputs; never let model output trigger actions without validation.                                                                            |
| Inappropriate or unsafe uploads                                   | Automatic safety check before analysis, content rules in the Terms of Use, and suspension of AI features for repeated violations (F9).                                          |
| AI free-tier quotas, rate limits, or cost spikes                  | Daily per-learner limits; AI features require a verified email; provider abstraction.                                                                                           |
| Abuse of public sign-up and uploads                               | Rate limiting, verified email for AI features, and per-learner usage limits.                                                                                                    |
| People under 18 sign up despite the age limit                     | A neutral year-of-birth question at sign-up, the age limit in the Terms of Use, promotion aimed at adults, and admins who suspend or delete accounts shown to belong to minors. |

## 12. Decisions on former open questions

| #   | Question                            | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| --- | ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Q1  | Target English proficiency          | CEFR A1–B1. Each extracted word is tagged with a CEFR level; word choice and explanations target A1–B1.                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Q2  | How Identify points to an object    | The learner's photo is shown with the object highlighted by a box returned by the AI.                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Q3  | Can learners fix AI-extracted words | Learners can remove wrong or unwanted words; editing word text is not supported in Release 1.0.                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Q4  | Photo retention and deletion        | Photos are kept until the learner deletes the world. Deleting a world removes its photo, words, and progress; deleting the account removes all learner data.                                                                                                                                                                                                                                                                                                                                                                  |
| Q5  | Per-learner usage limits            | Up to 20 worlds; 10 photo analyses per day; 30 tutor messages per day; photos up to 10 MB (JPEG, PNG, WebP).                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Q6  | Registration model                  | Open sign-up. Photo analysis and the AI tutor require a verified email; Google sign-in counts as verified.                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Q7  | Additional sign-in methods          | LINE and Facebook added to Release 1.0 (2026-09-19). Rules for these accounts, including those without an email address, are in the SRS (FR-003, FR-009, V13, V14).                                                                                                                                                                                                                                                                                                                                                           |
| Q8  | Content policy                      | Blocked photos count toward the daily analysis limit. Vocabulary covers objects and places only, never people or body parts. 3 blocked photos within 7 days pause AI features for 7 days; reaching it again keeps them off until an admin reviews the case in the moderation admin area.                                                                                                                                                                                                                                      |
| Q9  | Minimum age                         | Release 1.0 is for adults aged 18 or older (2026-09-19). Sign-up asks for the year of birth, the Terms of Use state the age limit, and LanguZe is described and promoted for adults. The chosen AI provider does not allow apps directed at or likely to be used by people under 18, and serving minors would need parental consent under Thailand's PDPA. Teenagers are on the roadmap.                                                                                                                                      |
| Q10 | When LanguZe opens to the public    | After Release 1.0, as a separate step (2026-10-07). Until then production is closed to the public and used only by the operator and invited testers. Thailand's PDPA and Computer Crime Act apply to any public service, whether or not it earns money, and meeting them needs a named data controller, a lawyer's answers, final legal text, and 90-day traffic records ([legal review](../deployment/legal-review.md)). Deferring that work lets Release 1.0 be finished and tested first; LanguZe is not run commercially. |

## 13. References

- Product planning notes (Thai): product concept, differentiation, stack, and roadmap decisions summarized in this document.
- [SRS](SRS.md)
- [ADR-0001: Modular monolith](../architecture/adr/0001-modular-monolith.md)
- [ADR-0002: Toolchain baseline](../architecture/adr/0002-toolchain-baseline.md)
