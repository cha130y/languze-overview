# Image to vocabulary flow

- **Use cases:** UC-010, UC-011, UC-020, UC-021, UC-022
- **Requirements:** FR-010–FR-017, FR-020–FR-027, FR-043, FR-091–FR-095, AIR-001–AIR-009, NFR-001, NFR-007–NFR-010
- **Decisions:** analysis runs as a background task in the API (P2); photos go through the API and are prepared before storage (P4, FR-017).

## 1. Create a world (UC-010)

```mermaid
sequenceDiagram
  autonumber
  actor L as Learner
  participant W as Web
  participant A as API
  participant D as Database
  participant S as Photo storage
  L->>W: Enter a name, take or choose a photo
  W->>A: Create world (name, photo file)
  A->>A: Check name, and file type from its content and size
  A->>A: Prepare photo - apply orientation, remove metadata, scale down (P5)
  A->>D: One transaction: lock learner's usage, check verified, AI suspension, world count, and daily analyses including those in progress, then create world ANALYZING and analysis IN_PROGRESS
  alt A check fails
    A-->>W: Refused with the reason (not verified, suspended, 20 worlds, or limit reached with reset time)
  else World created
    A->>S: Store the prepared photo under a random key
    alt Storage fails
      A->>D: Delete the world and the analysis
      A-->>W: Error, try again
    else Stored
      A-->>W: World created, ANALYZING
      A->>A: Start the analysis in the background (section 2)
    end
  end
```

- The limit check and the new analysis are saved in one transaction that locks the learner's usage (step 5), so two uploads at the same moment cannot both take the last analysis (FR-020, U2).
- The world and analysis are saved before the photo is stored, so a refusal never leaves a photo behind, and a storage failure is undone with a database delete (UC-010 minimal guarantee).

## 2. Analyse the photo (UC-020)

```mermaid
sequenceDiagram
  autonumber
  participant A as API background task
  participant S as Photo storage
  participant AI as AI provider
  participant D as Database
  participant E as Email service
  A->>S: Read the prepared photo, unless still in memory from the upload
  A->>AI: Safety check of the photo
  alt Photo blocked
    A->>D: One transaction: analysis BLOCKED (counts), world FAILED with Terms message, block record, photo key for cleanup, suspension check (moderation flow)
    A->>S: Delete the photo
    opt A case now needs review
      A->>E: Email admins
    end
  else Safety check error or timeout
    A->>D: Analysis FAILED (released), world FAILED
  else Photo passed
    A->>AI: Extract vocabulary (photo, fixed instructions, output schema)
    AI-->>A: Structured output
    A->>A: Validate and post-process (section 3)
    alt Provider error, invalid output, or fewer than 3 valid words
      A->>D: Analysis FAILED (released), world FAILED with advice
    else 3 to 12 valid words
      A->>D: One transaction: find or create vocabulary words, create occurrences, world READY, analysis SUCCEEDED (counts)
    end
  end
  A->>A: Log provider, model, latency, tokens, outcome (no photo, no prompt)
```

- The request to the AI provider contains the photo and fixed instructions only, never learner data (AIR-002, AIR-006). Text in the photo is image content, not instructions.
- A blocked photo is deleted from storage right after the transaction commits (step 4 after step 3). If deletion fails, the cleanup record keeps it on the retry list.
- The safety check and the extraction are two separate AI calls, safety first ([ADR-0004](../architecture/adr/0004-ai-provider.md)).
- If the world was deleted while the analysis ran, the result is discarded at the save step.
- **5-minute rule (V16):** when the API starts, and every minute afterwards, it marks analyses that have been `IN_PROGRESS` for more than 5 minutes as `FAILED` (released) and their worlds as `FAILED`. This catches analyses lost when the API restarts **(P2)**. The minute check reads the database only from the start of an analysis until a minute past its 5-minute mark, and for 6 minutes after the API starts; otherwise there is nothing to release, and reading anyway would keep Neon's compute from suspending ([production deployment, section 10](../deployment/production.md#10-cost)).

## 3. Validation and post-processing

The deterministic checks between the AI output and the database (AIR-003, AIR-004):

```mermaid
flowchart TD
  O["AI structured output"] --> P{"Matches the output schema?"}
  P -- no --> F1["Analysis FAILED: invalid output"]
  P -- yes --> I["Check each item"]
  I --> V{"Passes the item rules of AIR-003?"}
  V -- no --> X["Discard the item"]
  V -- yes --> H{"A word for a person or a body part?"}
  H -- yes --> X
  H -- no --> N["Normalize the word and its accepted variants"]
  N --> M["Merge duplicates in this photo, keeping one highlight box"]
  M --> C["Keep at most 12, preferring the clearest objects"]
  C --> K{"At least 3 items left?"}
  K -- no --> F2["Analysis FAILED: too few words"]
  K -- yes --> OK["Save the words, world READY"]
```

- Item rules (AIR-003): an English word of letters, spaces, or hyphens (1–40 characters); a Thai meaning containing Thai script; an example sentence containing the word, a regular plural of it, or a variant; a CEFR level from A1 to C2; a highlight box inside the image with a non-zero area; accepted variants unique after normalization.
- People and body parts are filtered twice: the model is instructed not to return them, and a fixed list of people and body-part words removes any that slip through (FR-094, AIR-009).
- To keep "the clearest objects", the output schema includes a prominence value for each item; the AI design defines it.

## 4. Showing the result (FR-021)

```mermaid
sequenceDiagram
  autonumber
  actor L as Learner
  participant W as Web
  participant A as API
  loop Every 3 seconds while an ANALYZING world is on screen
    W->>A: Get world status
    A-->>W: ANALYZING, READY, or FAILED
  end
  W-->>L: Show the words, or the failure with what to do next
```

Polling stops when the status is `READY` or `FAILED`, when the learner leaves the page, and after six minutes (P3). Six minutes is the 5-minute rule (V16) plus the minute the sweep can take to notice, so a page that has waited longer than that is waiting on something no amount of asking will fix: it says so and offers a refresh rather than spinning for ever.

Both the list of worlds and one world's page ask, so a learner who goes back to the list still sees the result arrive. Only worlds that are `ANALYZING` are asked about, and the status endpoint is used rather than reloading the page, because a reload signs new photo links each time and the pictures would flicker.

## 5. World status

```mermaid
stateDiagram-v2
  [*] --> ANALYZING: world created
  ANALYZING --> READY: 3 to 12 valid words saved
  ANALYZING --> FAILED: blocked, too few words, provider error, or 5 minutes passed
  FAILED --> ANALYZING: retry, only if the photo still exists
  READY --> [*]: world deleted
  FAILED --> [*]: world deleted
  ANALYZING --> [*]: world deleted, result discarded
```

Retrying (UC-021) repeats the checks and the transaction of section 1, step 5, for the existing world, then runs section 2 on the stored photo. A world whose photo was blocked has no photo, so it can only be deleted.

## 6. Deleting a world or removing a word (UC-011, UC-022)

```mermaid
sequenceDiagram
  autonumber
  actor L as Learner
  participant W as Web
  participant A as API
  participant D as Database
  participant S as Photo storage
  L->>W: Delete world and confirm
  W->>A: Delete world
  A->>D: One transaction: delete the world and its occurrences, delete mastery and mistake history of words in no other world, record the photo key for cleanup
  A-->>W: Deleted
  A->>S: Delete the photo, retried until done within 24 hours
```

Removing a word follows steps 2–4 for one occurrence, without the photo. Total XP never changes (V4).

## 7. Points for the data model and API design

- Each analysis is its own record with a start time and an outcome (`IN_PROGRESS`, `SUCCEEDED`, `BLOCKED`, `FAILED`). The daily count is the number of analyses started today (`Asia/Bangkok`) that are `IN_PROGRESS`, `SUCCEEDED`, or `BLOCKED`, so "releasing" an analysis just means it ends as `FAILED`.
- A vocabulary word belongs to the learner and is identified by its normalized English word and Thai meaning (FR-043); occurrences link it to a world with the highlight box, example sentence, CEFR level, and accepted variants.
- Highlight boxes are stored relative to the prepared photo (0–1), which is the photo the learner sees.
- Attempts and mistakes must keep their link to the vocabulary word when an occurrence or world is deleted, because a word still in another world keeps its mistake history (FR-015). Their link to the deleted occurrence is cleared, not cascaded.
- Total XP is stored on the learner and only increases; it is not recalculated from attempts, which can be deleted (V4).
- The API returns the photo as a short-lived signed link, never as a public address **(P4)**.
