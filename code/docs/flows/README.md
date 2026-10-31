# LanguZe — Process Flows

- **Document status:** Draft v0.1 — questions raised while writing, and their decisions, are in [Appendix A](#appendix-a-questions-raised-while-writing-the-flows)
- **Date:** 2026-09-19
- **Release covered:** Release 1.0
- **Source:** [Use cases](../requirements/use-cases.md) v0.3 and [SRS](../requirements/SRS.md) v0.8
- **Next documents:** architecture overview → ERD and data dictionary → ADRs → API design

## Purpose

Use cases describe what the user and LanguZe do. Process flows show how the parts of LanguZe carry that out: which component does what, in which order, where database transactions begin and end, and when external systems are called. They are the input for the architecture overview, the data model, and the API design.

Flows describe behavior, not endpoints or table names. Routes, request formats, and the schema are defined later in the API design and the ERD; each flow ends with the points those documents must respect.

## Flows

| Flow                                          | Covers                                                                            | Use cases                     |
| --------------------------------------------- | --------------------------------------------------------------------------------- | ----------------------------- |
| [Authentication](authentication.md)           | Sign-up, verification, provider sign-in and linking, request authorization, reset | UC-001–UC-005                 |
| [Image to vocabulary](image-to-vocabulary.md) | World creation, photo preparation, background analysis, status updates, deletion  | UC-010, UC-011, UC-020–UC-022 |
| [Game session](game-session.md)               | Starting or continuing a session, answering, mastery, review                      | UC-030, UC-031, UC-050        |
| [AI tutor](ai-tutor.md)                       | Messages, tool calls, streaming, the daily limit                                  | UC-070                        |
| [Moderation](moderation.md)                   | Automatic AI suspension, admin actions, admin notifications                       | UC-090, UC-101–UC-103         |

Four use cases need no flow of their own: UC-060 and UC-104 only read data, UC-080 is configuration read when the API starts, and UC-100 is a script the operator runs.

## Participants

| Participant       | What it is                                                                                                                                          |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Learner, Admin    | A person using a browser.                                                                                                                           |
| Web               | The Next.js application (`apps/web`). It renders pages and calls the API; it never talks to the database, photo storage, or AI providers (SRS 2.1). |
| API               | The NestJS application (`apps/api`), organized in domain modules (ADR-0001).                                                                        |
| Database          | PostgreSQL, used only by the API through Prisma.                                                                                                    |
| Photo storage     | Private object storage for photos.                                                                                                                  |
| AI provider       | The photo safety check, vocabulary extraction, and tutor model, reached only through the API's AI interface (AIR-001).                              |
| Email service     | Sends verification, password reset, and admin notification emails.                                                                                  |
| Identity provider | Google, LINE Login, or Facebook Login.                                                                                                              |

## Rules for every flow

- **Who is asking:** the API takes the learner from the session on every request, never from request input, and authorizes every request (FR-008). See [Authentication, section 3](authentication.md#3-authorizing-every-request).
- **Transactions:** changes that must stay consistent are saved in one database transaction, for example an attempt with its mastery update and XP, or an admin action with its audit entry.
- **Side effects after commit:** emails and photo deletions happen only after the transaction that establishes the facts has committed. If a side effect fails, the facts stay; the failure is logged, and photo deletions are retried until they succeed.
- **No AI calls inside transactions:** AI provider calls are slow and can fail, so they never run while a database transaction is open.
- **Daily limits:** checking a daily limit and recording the new use happen in one transaction that locks the learner's usage, so parallel requests cannot both take the last remaining use. Days use the `Asia/Bangkok` time zone (V1).
- **Untrusted data:** photos, AI output, and text typed by learners are data, never instructions (NFR-007, AIR-002).

## Notation

Diagrams use [Mermaid](https://mermaid.js.org/), which GitHub renders. Sequence diagrams show numbered messages between participants; solid arrows are requests and dashed arrows are responses. State diagrams show the allowed changes of a status. References such as (P2) point to the decisions in Appendix A.

## Appendix A. Questions raised while writing the flows

These are mostly architecture choices. All were decided on 2026-09-19. P5 is recorded in SRS v0.8 as FR-017; the others are recorded in the [architecture overview](../architecture/overview.md) and [ADR-0003](../architecture/adr/0003-better-auth-in-api.md).

| ID  | Question                                                                                                                                          | Decision                                                                                                                                                                                                                                                                                                                                                                                                                  | Affects                                       | Status               |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- | -------------------- |
| P1  | Where does authentication (Better Auth) run?                                                                                                      | Inside the NestJS API, as its auth module, with its tables in the API's Prisma schema. The web app reaches authentication only through the API. SRS 2.1 already says the API is the only component that talks to the database, and NestJS guards can then read sessions directly. Running it in Next.js would give the web app database access and split the schema between two applications.                             | All flows; Better Auth ADR                    | Confirmed 2026-09-19 |
| P2  | How does photo analysis run in the background?                                                                                                    | As a task inside the API process, started after the upload response. Its status is kept in the database, and the 5-minute rule (V16) marks analyses lost in a restart as `FAILED`, which the learner can retry. No job queue and no Redis in Release 1.0; add them when load or reliability requires it.                                                                                                                  | Image to vocabulary; architecture overview    | Confirmed 2026-09-19 |
| P3  | How does the web app learn that an analysis has finished?                                                                                         | It asks the API for the world's status every 3 seconds while an `ANALYZING` world is on screen, and stops when it is `READY` or `FAILED`, the learner leaves, or six minutes have passed (the 5-minute rule of V16 plus the sweep's minute). Analysis takes about 20 seconds, so polling costs little and works in every browser, including in-app browsers. Server push (server-sent events or WebSocket) is not needed. | Image to vocabulary; API design               | Confirmed 2026-09-19 |
| P4  | How do photos travel?                                                                                                                             | Upload through the API, which checks the file content and prepares the photo (P5) before storing it. Photos are shown through short-lived signed links from photo storage, so they stay private (NFR-008) without the API relaying every image. The alternatives are direct upload from the browser to storage, which stores the file before LanguZe can check it, or the API relaying every photo view, which adds load. | Image to vocabulary, game session; API design | Confirmed 2026-09-19 |
| P5  | Phone photos contain metadata, including the GPS location where they were taken, which for most worlds is the learner's home. What happens to it? | Before storing, the API applies the photo's orientation, removes all metadata, and scales it down so the longer side is at most 2,048 pixels, then stores only this prepared photo. This protects the learner's location, makes highlight boxes line up with the photo as displayed, and lowers AI cost and time. Recorded as FR-017.                                                                                     | FR-017, V18, image to vocabulary              | Confirmed 2026-09-19 |
| P6  | How does the tutor's reply reach the browser?                                                                                                     | Streamed over HTTP with server-sent events, so the learner sees the reply as it is written (NFR-003). This is one-way streaming over a normal request and works in in-app browsers. WebSocket (Socket.IO) waits for real-time features such as multiplayer.                                                                                                                                                               | AI tutor; API design                          | Confirmed 2026-09-19 |
