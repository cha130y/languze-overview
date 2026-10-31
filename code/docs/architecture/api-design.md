# LanguZe — API Design

- **Document status:** Draft v0.1 — questions raised while writing, and their decisions, are in [Appendix A](#appendix-a-questions-raised-while-writing-the-api-design)
- **Date:** 2026-09-19
- **Release covered:** Release 1.0
- **Source:** [SRS](../requirements/SRS.md) v0.10, [use cases](../requirements/use-cases.md), [process flows](../flows/README.md), [data model](data-model.md), [ADR-0003](adr/0003-better-auth-in-api.md), [ADR-0004](adr/0004-ai-provider.md)
- **Next:** implementation, starting with the Prisma schema, the first migration, and the `auth` module

## 1. Purpose

This document defines the HTTP API between the web app and the API for Release 1.0: conventions, every LanguZe endpoint, error codes, and limits. The OpenAPI document generated from the NestJS controllers (served at `/docs` outside production) becomes the exact contract once the endpoints exist; this document is the plan it is built from and is kept in step with it.

## 2. Conventions

### 2.1 Paths and versions

- LanguZe endpoints live under `/v1` (E1). Changes within `v1` are additive only: new endpoints, new optional request fields, new response fields. A breaking change would need `/v2`.
- Better Auth's own endpoints live under `/auth`, and only the two that provider sign-in needs are served (section 3.1, ADR-0003); every other path there answers `NOT_FOUND`.
- `GET /health` (already implemented) and `/docs` (OpenAPI, not in production) stay at the root.
- Resource names are plural nouns; IDs in paths are UUIDs. A request for another learner's resource returns `404`, never `403`, so it reveals nothing about what exists (FR-008).

### 2.2 Requests and responses

- JSON with `camelCase` field names, except the photo upload (`multipart/form-data`) and the tutor stream (`text/event-stream`).
- Times are ISO 8601 strings in UTC, such as `2026-09-19T07:30:00Z`. Values that are calendar days in `Asia/Bangkok` (V1) are `YYYY-MM-DD` strings.
- Enum values are the upper-case names from the [data model](data-model.md#6-enums), such as `READY` or `FAMILIAR`.
- Request bodies are validated at the boundary with class-validator DTOs; unknown fields are rejected (already configured). Limits come from the SRS, including V19.
- Lists that can grow without bound (tutor messages, the audit log) use cursor pagination: `?limit=` (default 20, at most 50) and `?cursor=`, with `nextCursor` in the response, `null` on the last page. Lists with a small fixed maximum (at most 20 worlds, 10 questions) are returned whole.
- Photos are returned as short-lived signed links, never as permanent addresses (P4).

### 2.3 Sessions and cross-site protection

- The session is Better Auth's cookie, sent by the browser automatically: `HttpOnly`, `Secure`, `SameSite=Lax`, shared across LanguZe subdomains in production (A1, ADR-0003).
- CORS allows only the web app's origin, with credentials. The current CORS setup must add `credentials: true`.
- Every request that changes data must carry an allowed `Origin` header. With `SameSite=Lax` cookies, this blocks cross-site request forgery even for form-style requests such as the photo upload.
- Every endpoint requires a signed-in account unless marked **public**. Status, role, verification, and AI suspension are read from the database on each request (NFR-019).

### 2.4 Errors

Every error has the same shape (E2):

```json
{
  "error": {
    "code": "DAILY_ANALYSIS_LIMIT",
    "message": "The daily photo analysis limit has been reached.",
    "details": { "resetsAt": "2026-09-19T17:00:00Z" }
  }
}
```

- `code` is stable and machine-readable; the web app shows Thai text for each code (NFR-013).
- `message` is English, for developers and logs; it is never shown to learners as is.
- `details` is optional and specific to the code.
- Validation errors use `VALIDATION_FAILED` with `details.fields`, a list of the fields and the rules they broke.

| Status | Used for                                                                                           |
| ------ | -------------------------------------------------------------------------------------------------- |
| `400`  | The request is malformed or breaks a validation rule.                                              |
| `401`  | No valid session.                                                                                  |
| `403`  | Signed in, but not allowed: account suspended, not an admin, not verified, AI suspended, under 18. |
| `404`  | The resource does not exist or belongs to someone else.                                            |
| `409`  | The resource is in the wrong state for the request, such as retrying a world that is `READY`.      |
| `413`  | The photo is larger than 10 MB.                                                                    |
| `415`  | The photo is not JPEG, PNG, or WebP.                                                               |
| `429`  | A rate limit or a daily limit is reached; `Retry-After` and `details.resetsAt` say when to retry.  |
| `500`  | Unexpected error. The response carries the request ID so the error can be found in the logs.       |
| `503`  | A required service (database, AI provider, photo storage) is unavailable.                          |

## 3. Endpoints

### 3.1 Authentication (`/v1/auth`)

Better Auth runs inside the API (ADR-0003), but learners reach it through LanguZe endpoints, so every answer uses the error format of section 2.4 and the rate limits of section 5.

| Method | Path                               | Purpose                                                                                                                             | Requirements                |
| ------ | ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | --------------------------- |
| `POST` | `/v1/auth/sign-up`                 | Email, password, display name, year of birth, and accepting the Terms; signs the learner in and sends the verification email; `201` | FR-001, FR-004, FR-090, V20 |
| `POST` | `/v1/auth/sign-in`                 | Email and password                                                                                                                  | FR-002, FR-105              |
| `POST` | `/v1/auth/sign-out`                | Ends this browser's session; `204`                                                                                                  | FR-002                      |
| `POST` | `/v1/auth/send-verification-email` | Sends a new verification email                                                                                                      | FR-004                      |
| `POST` | `/v1/auth/verify-email`            | Verifies the address with the token from the email                                                                                  | FR-004                      |
| `POST` | `/v1/auth/request-password-reset`  | Sends a reset link                                                                                                                  | FR-005                      |
| `POST` | `/v1/auth/reset-password`          | Sets a new password and ends every session                                                                                          | FR-005, S7                  |
| `GET`  | `/v1/auth/providers`               | Which provider sign-ins this API offers, in the order to show them                                                                  | FR-003                      |

- Sign-up and sign-in answer with the account, in the same shape as `GET /v1/me`, and set the session cookie.
- Sign-up is refused with `TERMS_NOT_ACCEPTED` (`400`), `AGE_BELOW_MINIMUM` (`403`, V20), or `EMAIL_ALREADY_REGISTERED` (`409`, U7). An under-age answer also sets a short `HttpOnly` cookie that blocks further attempts from that browser for 24 hours. A placeholder address (D1) is refused as invalid, so nobody can claim the address a provider identity resolves to.
- `/v1/auth/providers` answers `{ "providers": ["google", "line"] }` and needs no session. It lists the providers whose credentials are configured, so the web app never shows a button that cannot work.
- Sign-in is refused with `INVALID_CREDENTIALS` (`401`), which never says whether the address is known, or `ACCOUNT_SUSPENDED` (`403`) once the password has confirmed the person (FR-105).
- The two email endpoints always answer `{ "ok": true }`, whether or not the address has an account (FR-005).
- Verification and reset links point at the web app, which sends the token to the API. A used or expired token is answered with `INVALID_TOKEN` (`400`).
- `DELETE /v1/me` answers `204` and signs the browser out. The confirmation word is the API's own guard, so one stray request can never delete an account; the learner confirms on screen as well (FR-007, US-009). Anything else in `confirmation` is `VALIDATION_FAILED` (`400`) and deletes nothing. Everything belonging to the account goes with it through the schema's cascades; stored photos are removed within 24 hours by the storage module (NFR-009, V10).

Provider sign-in is the exception: the provider sends the browser straight back to the API, so these two routes are Better Auth's own and answer in its format. Nothing else of Better Auth's is served — its own sign-up, for example, would skip the age check and the Terms.

| Method | Path                        | Purpose                                                                                                                                      | Requirements           |
| ------ | --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------- |
| `POST` | `/auth/sign-in/social`      | Starts a provider sign-in and answers the provider's address, which the web app opens                                                        | FR-003                 |
| `GET`  | `/auth/callback/{provider}` | The provider's return. Signs the learner in and redirects to the web app: home, the Terms step for a new account, or `/sign-in?error=<code>` | FR-003, FR-009, FR-090 |

### 3.2 Account (`auth` module)

| Method   | Path                   | Purpose                                                                                    | Requirements           |
| -------- | ---------------------- | ------------------------------------------------------------------------------------------ | ---------------------- |
| `GET`    | `/v1/me`               | The signed-in account and what it may do                                                   | FR-006, FR-096, FR-108 |
| `POST`   | `/v1/me/terms`         | Complete a pending provider sign-up: accept the Terms, confirm display name, year of birth | FR-090, V20            |
| `POST`   | `/v1/me/terms/decline` | Decline: removes the pending sign-up and signs out                                         | FR-090                 |
| `GET`    | `/v1/me/usage`         | Analyses left today and when they reset; tutor messages follow in their own increment      | FR-080                 |
| `DELETE` | `/v1/me`               | Delete the account; body `{ "confirmation": "DELETE" }`                                    | FR-007                 |

`GET /v1/me` returns:

```json
{
  "id": "0192f0c1-8c1e-7cc3-9d2a-5b3e1c4a7f10",
  "name": "Nok",
  "email": "nok@example.com",
  "emailVerified": true,
  "verifiedForAi": true,
  "role": "LEARNER",
  "termsAccepted": true,
  "aiAccess": {
    "available": true,
    "reason": null,
    "until": null,
    "awaitingReview": false
  }
}
```

- `id` is the account ID learners can quote to LanguZe (FR-108).
- `email` is `null` for accounts with a placeholder address (D1).
- While `termsAccepted` is `false` (a pending provider sign-up), every other endpoint returns `TERMS_PENDING` (`403`).
- `aiAccess.reason` is `NOT_VERIFIED` or `AI_SUSPENDED` when AI features are unavailable.

### 3.3 Worlds (`worlds` module)

| Method   | Path                                        | Purpose                                                              | Requirements                  |
| -------- | ------------------------------------------- | -------------------------------------------------------------------- | ----------------------------- |
| `GET`    | `/v1/worlds`                                | List worlds with status, thumbnail link, word and mastered counts    | FR-013                        |
| `POST`   | `/v1/worlds`                                | Create a world: `multipart/form-data` with `name` and `photo`; `202` | FR-010–FR-012, FR-017, FR-020 |
| `GET`    | `/v1/worlds/{worldId}`                      | Photo link, status, and words with meaning, CEFR level, mastery, box | FR-014                        |
| `GET`    | `/v1/worlds/{worldId}/status`               | Status only, for polling every 3 seconds (P3)                        | FR-021                        |
| `PATCH`  | `/v1/worlds/{worldId}`                      | Rename: `{ "name": "..." }`                                          | FR-016                        |
| `DELETE` | `/v1/worlds/{worldId}`                      | Delete the world; `204`                                              | FR-015                        |
| `POST`   | `/v1/worlds/{worldId}/retry`                | Retry a failed analysis on the same photo; `202`                     | FR-025                        |
| `DELETE` | `/v1/worlds/{worldId}/words/{occurrenceId}` | Remove a word from the world; `204`                                  | FR-026, FR-027                |

- Creating a world returns `202 Accepted` with the world in `ANALYZING`; the analysis runs in the background (P2) and the words appear when it finishes. The daily limit is decided before the photo is stored, so a learner who has none left ends up with no world and no file (FR-020).
- A world carries `thumbnailUrl` and, when opened, `photoUrl`: signed links that work for a few minutes and only for that object (P4). They are empty when the photo is gone.
- Uploads are `multipart/form-data` and are kept in memory only; the file is prepared before anything is stored, and the original is never written down (FR-017).
- Creation and retry can fail with `NOT_VERIFIED` or `AI_SUSPENDED` (`403`), `WORLD_LIMIT_REACHED` (`409`), `DAILY_ANALYSIS_LIMIT` (`429`), `PHOTO_TOO_LARGE` (`413`), or `PHOTO_TYPE_NOT_ALLOWED` (`415`).
- Retry fails with `RETRY_NOT_AVAILABLE` (`409`) when the world is not `FAILED` or its photo was blocked.
- Removing the last word fails with `LAST_WORD` (`409`).
- A `FAILED` world carries `failureReason` (`BLOCKED`, `TOO_FEW_WORDS`, `PROVIDER_ERROR`, `INVALID_OUTPUT`, `TIMED_OUT`), which the web app turns into the right message and actions.

### 3.4 Practice sessions (`learning` module)

| Method | Path                                                        | Purpose                                                                                                                         | Requirements                  |
| ------ | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| `GET`  | `/v1/sessions/current?kind=GAME&worldId=` or `?kind=REVIEW` | The open session within 24 hours, if any; `204` when there is none                                                              | FR-036, V17                   |
| `POST` | `/v1/sessions`                                              | Start a session: `{ "kind": "GAME", "worldId": "..." }` or `{ "kind": "REVIEW" }`; abandons an open one of the same kind; `201` | FR-030, FR-050, FR-051        |
| `GET`  | `/v1/sessions/{sessionId}`                                  | Progress, the next question, and the summary once completed                                                                     | FR-031, FR-035, FR-036        |
| `POST` | `/v1/sessions/{sessionId}/questions/{questionId}/answer`    | Answer: `{ "answer": "sofa" }` or `{ "dontKnow": true }`                                                                        | FR-032–FR-034, FR-042, FR-060 |

A session's next question never includes the answer (FR-032):

```json
{
  "id": "…",
  "kind": "GAME",
  "status": "IN_PROGRESS",
  "worldId": "…",
  "answeredCount": 4,
  "questionCount": 10,
  "nextQuestion": {
    "id": "…",
    "position": 5,
    "photoUrl": "https://…signed…",
    "box": { "x": 0.12, "y": 0.4, "width": 0.25, "height": 0.2 }
  },
  "startedAt": "2026-09-25T04:00:00.000Z",
  "summary": null
}
```

An answer returns the feedback, and the summary when it was the last question:

```json
{
  "correct": false,
  "dontKnow": false,
  "alreadyAnswered": false,
  "answer": "settee",
  "matchedVariant": false,
  "word": {
    "english": "sofa",
    "thaiMeaning": "โซฟา",
    "exampleSentence": "I sit on the sofa."
  },
  "xpAwarded": 0,
  "mastery": { "before": "FAMILIAR", "after": "LEARNING" },
  "sessionCompleted": false,
  "summary": null
}
```

- `worldId` is empty for a review, and `startedAt` lets the page say how long ago a session was left, which matters because it closes after 24 hours (V17).
- `GET /v1/sessions/current` refuses a `kind` the API does not serve rather than answering with the other kind.
- `answer` is what the learner typed, as recorded, and empty for "I don't know". `matchedVariant` says a right answer was one of the word's accepted variants rather than the word itself. The page shows the answer beside the word when the two differ (FR-033).
- Answering the same question again returns the recorded result with `alreadyAnswered: true` and changes nothing (FR-034, S5). Its `answer` is the one recorded first.
- `WORLD_NOT_READY` (`409`), `NOTHING_TO_REVIEW` (`409`, FR-053), `SESSION_CLOSED` (`409`), and `QUESTION_UNAVAILABLE` (`409`, word removed since the session started) cover the other states.
- The summary lists the correct count, the XP earned, and the words whose level changed with their new level (FR-035).

### 3.5 Progress (`progress` module)

| Method | Path           | Purpose                                                                          | Requirements   |
| ------ | -------------- | -------------------------------------------------------------------------------- | -------------- |
| `GET`  | `/v1/progress` | Total XP, words at each mastery level, and each world's word and mastered counts | FR-060, FR-061 |

### 3.6 Tutor (`tutor` module)

| Method   | Path                 | Purpose                                                | Requirements           |
| -------- | -------------------- | ------------------------------------------------------ | ---------------------- |
| `GET`    | `/v1/tutor/messages` | The conversation, newest first, with cursor pagination | FR-070                 |
| `POST`   | `/v1/tutor/messages` | Send `{ "content": "..." }`; the reply streams back    | FR-070–FR-075, NFR-003 |
| `DELETE` | `/v1/tutor/messages` | Clear the conversation; `204`                          | FR-076                 |

Sending a message answers with `text/event-stream` (P6), with these events:

| Event   | Data                                                                          |
| ------- | ----------------------------------------------------------------------------- |
| `delta` | `{ "text": "..." }`, the next part of the reply                               |
| `done`  | `{ "messageId": "…", "replyId": "…", "messagesLeft": 21 }`                    |
| `error` | `{ "code": "AI_PROVIDER_UNAVAILABLE" }`; nothing is saved or counted (FR-071) |

- Browsers can only open event streams for `GET` requests with `EventSource`, so the web app reads this `POST` response with `fetch` and a stream reader.
- Before the stream starts, the request can fail with `NOT_VERIFIED` or `AI_SUSPENDED` (`403`), `MESSAGE_TOO_LONG` (`400`, V7), or `DAILY_TUTOR_LIMIT` (`429`).

### 3.7 Administration (`moderation` module, `/v1/admin`)

All admin endpoints require the `ADMIN` role, checked on every request; anyone else gets `404`, so the admin area is invisible to them (FR-100, FR-102, NFR-019).

| Method | Path                                         | Purpose                                                                          | Requirements   |
| ------ | -------------------------------------------- | -------------------------------------------------------------------------------- | -------------- |
| `GET`  | `/v1/admin/review-queue`                     | Cases awaiting review, oldest first, with block counts and categories            | FR-103         |
| `GET`  | `/v1/admin/learners?email=` or `?accountId=` | Find one learner by exact email or account ID; `404` when none                   | FR-108         |
| `GET`  | `/v1/admin/learners/{learnerId}`             | The moderation record: blocks, suspensions, account age, sign-in methods, status | FR-104         |
| `POST` | `/v1/admin/learners/{learnerId}/actions`     | `{ "action": "SET_AI_SUSPENSION_END", "reason": "...", "endsAt": "..." }`        | FR-105, FR-106 |
| `GET`  | `/v1/admin/audit-log`                        | Admin actions, newest first, with cursor pagination                              | FR-106         |

- The moderation record never contains photos, tutor messages, worlds, or words (FR-104).
- Actions fail with `OWN_ACCOUNT` (`403`), `ACTION_NOT_APPLICABLE` (`409`, for example reactivating an active account), or `VALIDATION_FAILED` for a missing reason or an end date in the past.
- An action answers `201` with the audit entry it wrote: the one thing every action has in common, since a deleted account has no record left to show. A reason is trimmed before it is kept, and must say something other than spaces.
- `endsAt` belongs to `SET_AI_SUSPENSION_END` alone: it is required there and refused with any other action, since doing the other action anyway would hide a client that has lost track of what it is asking for. An empty `endsAt` counts as none.
- The audit log comes 50 entries to a page, newest first, ordered by time and then by id so that entries made in the same millisecond keep one order.
- Searching by a placeholder email address finds nothing; learners without an email address are found by account ID (D1).
- A search answers with the learner's moderation record itself, the same as `GET /v1/admin/learners/{learnerId}`, rather than with a pointer to it. The admin searched in order to see it, and the record holds nothing the search did not already entitle them to.
- Sign-in methods in the record are `email`, `google`, `line` and `facebook`. A password is `email`, where Better Auth would call it `credential`.

### 3.8 Requirements served without an endpoint of their own

| Requirements                         | Where they are served                                                                                                                                                                                |
| ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| FR-002–FR-005, FR-009                | Better Auth endpoints and LanguZe hooks (section 3.1, ADR-0003)                                                                                                                                      |
| FR-022–FR-024, FR-091–FR-095, FR-098 | The background analysis started by `POST /v1/worlds` and `POST /v1/worlds/{worldId}/retry`; results appear through the world endpoints ([image to vocabulary flow](../flows/image-to-vocabulary.md)) |
| FR-040, FR-041, FR-043, FR-052       | Applied inside the answer endpoint and the analysis, visible as `mastery` in their responses ([game session flow](../flows/game-session.md))                                                         |
| FR-097, FR-107                       | The admin endpoints of section 3.7, and emails to admins sent by the API ([moderation flow](../flows/moderation.md))                                                                                 |
| FR-081                               | Environment configuration read at startup (UC-080)                                                                                                                                                   |
| FR-099                               | Web pages: the Terms of Use, the Privacy Policy, and the contact address                                                                                                                             |
| FR-101                               | The promotion script run by the operator (UC-100)                                                                                                                                                    |

## 4. Error codes

The generic codes (`BAD_REQUEST`, `FORBIDDEN`, `CONFLICT`, `PAYLOAD_TOO_LARGE`, `UNSUPPORTED_MEDIA_TYPE`, `SERVICE_UNAVAILABLE`) are used when the framework or shared infrastructure raises an error without a more specific code. The list lives in `apps/api/src/platform/errors/error-codes.ts`.

| Code                       | Status | Meaning                                                                                |
| -------------------------- | ------ | -------------------------------------------------------------------------------------- |
| `BAD_REQUEST`              | 400    | The request is malformed, for example a body that is not valid JSON.                   |
| `VALIDATION_FAILED`        | 400    | A field breaks a rule; `details.fields` lists them.                                    |
| `TERMS_NOT_ACCEPTED`       | 400    | Sign-up without accepting the Terms of Use.                                            |
| `MESSAGE_TOO_LONG`         | 400    | A tutor message over 1,000 characters (V7).                                            |
| `NOT_SIGNED_IN`            | 401    | No valid session.                                                                      |
| `INVALID_CREDENTIALS`      | 401    | The email address or password is incorrect (FR-002).                                   |
| `INVALID_TOKEN`            | 400    | A verification or reset link is used or expired (FR-004, FR-005).                      |
| `ACCOUNT_SUSPENDED`        | 403    | The account is suspended (FR-105).                                                     |
| `TERMS_PENDING`            | 403    | A pending provider sign-up must accept the Terms first (FR-090).                       |
| `AGE_BELOW_MINIMUM`        | 403    | The year of birth is under the limit (V20).                                            |
| `NOT_VERIFIED`             | 403    | AI features need a verified account (FR-006).                                          |
| `AI_SUSPENDED`             | 403    | AI features are suspended; `details` has the end time or review status (FR-096).       |
| `OWN_ACCOUNT`              | 403    | An admin action on the admin's own account (FR-105).                                   |
| `FORBIDDEN`                | 403    | Not allowed, when no more specific code applies.                                       |
| `ORIGIN_NOT_ALLOWED`       | 403    | A request that changes data did not come from the web app (section 2.3).               |
| `NOT_FOUND`                | 404    | Missing, someone else's, or an admin route for a non-admin.                            |
| `EMAIL_ALREADY_REGISTERED` | 409    | Sign-up with an address that has an account (FR-001).                                  |
| `WORLD_LIMIT_REACHED`      | 409    | The learner already has 20 worlds (FR-012).                                            |
| `WORLD_NOT_READY`          | 409    | A game for a world that is not `READY`.                                                |
| `RETRY_NOT_AVAILABLE`      | 409    | Retry of a world that is not `FAILED` or whose photo was blocked.                      |
| `LAST_WORD`                | 409    | Removing a world's last word (V5).                                                     |
| `NOTHING_TO_REVIEW`        | 409    | No word qualifies for review (FR-053).                                                 |
| `SESSION_CLOSED`           | 409    | The session is completed or abandoned.                                                 |
| `QUESTION_UNAVAILABLE`     | 409    | The question's word was removed.                                                       |
| `ACTION_NOT_APPLICABLE`    | 409    | The admin action does not fit the learner's state.                                     |
| `CONFLICT`                 | 409    | The resource is in the wrong state, when no more specific code applies.                |
| `PAYLOAD_TOO_LARGE`        | 413    | The request body is too large, when no more specific code applies.                     |
| `PHOTO_TOO_LARGE`          | 413    | Over 10 MB (FR-011).                                                                   |
| `PHOTO_TYPE_NOT_ALLOWED`   | 415    | Not JPEG, PNG, or WebP by content (FR-011).                                            |
| `UNSUPPORTED_MEDIA_TYPE`   | 415    | The body type is not accepted, when no more specific code applies.                     |
| `RATE_LIMITED`             | 429    | Too many requests in a short time (NFR-005).                                           |
| `DAILY_ANALYSIS_LIMIT`     | 429    | No photo analyses left today (FR-020).                                                 |
| `DAILY_TUTOR_LIMIT`        | 429    | No tutor messages left today (FR-071).                                                 |
| `AI_PROVIDER_UNAVAILABLE`  | 503    | The AI provider failed; nothing was counted (FR-025, FR-071).                          |
| `SERVICE_UNAVAILABLE`      | 503    | A required service, such as the database, is unavailable; `GET /health` reports which. |
| `INTERNAL_ERROR`           | 500    | Unexpected; `details.requestId` identifies it in the logs.                             |

## 5. Rate limits and other defaults

Starting values, all configurable per environment (E3). Rate limits are kept in the API's memory (A2) and answer `RATE_LIMITED` with `Retry-After`.

A limit counted against the wrong thing fails quietly — every request is still counted and every limit still answers — so two points decide whether the table below means anything. The account comes from the session the session guard has already resolved, which is why the guards are registered in that order. The address comes from `X-Forwarded-For`, counting back `TRUST_PROXY_HOPS` proxies; with the wrong number, either everyone behind one proxy shares a budget or a caller can invent an address and have as many budgets as it likes.

| What                                    | Limit                     | Keyed by                       |
| --------------------------------------- | ------------------------- | ------------------------------ |
| Email sign-in                           | 5 attempts per 15 minutes | Email address and IP           |
| Sign-up                                 | 5 per hour                | IP                             |
| Password reset and verification emails  | 3 per hour                | Email address                  |
| Photo upload and retry                  | 5 per minute              | Account                        |
| Tutor messages                          | 10 per minute             | Account                        |
| Admin actions                           | 30 per minute             | Admin account                  |
| Any other request                       | 120 per minute            | Account, or IP when signed out |
| Provider sign-in and callback (`/auth`) | 120 per minute            | IP                             |
| Lifetime of a signed photo link         | 15 minutes                | —                              |

The daily limits of the SRS (10 analyses, 30 tutor messages) are separate and apply on top of these.

## 6. Contract between web and API

- The NestJS controllers and DTOs, with their OpenAPI decorators, are the source of the contract; the OpenAPI document is generated from them.
- The web app's request and response types are generated from that OpenAPI document (E4), so a change in the API shows up as a type error in the web app instead of a bug at runtime.
- Each endpoint gets an API end-to-end test that covers its success case and its main error codes; the acceptance criteria of the stories list them.

## Appendix A. Questions raised while writing the API design

| ID  | Question                                             | Decision                                                                                                                                                                                                                                                                                                                                                     | Affects                  | Status               |
| --- | ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------ | -------------------- |
| E1  | How are paths laid out?                              | LanguZe endpoints under `/v1`, Better Auth under `/auth`, and `/health` and `/docs` at the root. Better Auth's default is `/api/auth`, which would repeat "api" on the `api.` subdomain; ADR-0003 now says `/auth`.                                                                                                                                          | All endpoints; ADR-0003  | Confirmed 2026-09-19 |
| E2  | What does an error look like?                        | One shape everywhere: a stable `code`, an English `message` for developers, and optional `details`. The web app shows Thai text for each code, so wording can change without an API release, and a later English interface needs no API change.                                                                                                              | All endpoints; web app   | Confirmed 2026-09-19 |
| E3  | What are the starting rate limits and link lifetime? | The values in section 5. They are strict enough to stop password guessing and email flooding, loose enough that a learner playing quickly never meets them, and all can be changed per environment.                                                                                                                                                          | Section 5; configuration | Confirmed 2026-09-19 |
| E4  | How does the web app get the API's types?            | Generate TypeScript types from the OpenAPI document with `openapi-typescript`, a development-only dependency of the web app, run by a script whenever the API changes. The alternatives are hand-written types, which drift from the API, or a shared package, which ADR-0001 reserves for stable contracts and which the OpenAPI document already provides. | Web app; tooling         | Confirmed 2026-09-19 |
