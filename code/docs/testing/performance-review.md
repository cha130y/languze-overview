# Performance review for Release 1.0

- **Status:** NFR-002 met (2026-10-08). NFR-003 met in production (2026-10-23, section 2). NFR-001 to be measured in production once learners have made worlds (section 3)
- **Requirements:** [NFR-001, NFR-002, NFR-003](../requirements/SRS.md), with the expected load V22
- **Used by:** [launch checklist, section 5](../deployment/launch-checklist.md#5-reviews)

## Summary

| Requirement                                                                                          | Result                                                                                                       | How                                  |
| ---------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ------------------------------------ |
| **NFR-002** Non-AI requests, p95 ≤ 500 ms with 25 learners at once, about 10 requests a second (V22) | **Met**: p95 25 ms over 1,909 requests at 9.1 a second, no failures; the slowest endpoint 75 ms              | k6 load test, section 1              |
| **NFR-003** The tutor's reply starts within 10 s at p95                                              | **Met in production**: whole replies p95 2.0 s over 21, so the first piece sooner still                      | `ai_calls` in production (section 2) |
| **NFR-001** From a finished upload to `READY`, p95 ≤ 20 s                                            | **Expected to hold**: the safety check and extraction take p95 6.9 s together. To be confirmed in production | Section 3                            |

## 1. NFR-002: the load test

**What it does.** [`apps/api/load/learners.js`](../../apps/api/load/learners.js) runs in [k6](https://grafana.com/docs/k6/latest/). Each virtual user is one learner, signed in once with the password, who then plays rounds:

1. their account and their worlds
2. one world, and starting a game in it
3. three questions answered, the session read after each
4. their progress, and whether a review is open

Between requests the learner pauses 1.5 to 3.5 seconds. With 25 learners that comes to about 10 requests a second, the expected load of V22.

**Thresholds.** The run fails unless:

- every endpoint answers within 500 ms at the 95th percentile, both on its own and across all of them
- fewer than 1% of the requests fail

**Sign-in is measured but not held to 500 ms.** It checks a scrypt hash, which is slow on purpose.

### Results (2026-10-08)

The API ran as the production build (`node dist/main.js`) on the developer's machine, with PostgreSQL 18 in Docker, and k6 ran from its Docker image.

| Endpoint                               | p50   | p95       | max   |
| -------------------------------------- | ----- | --------- | ----- |
| `GET /v1/me`                           | 12 ms | 75 ms     | 80 ms |
| `GET /v1/worlds`                       | 14 ms | 19 ms     | 25 ms |
| `GET /v1/worlds/:id`                   | 15 ms | 20 ms     | 39 ms |
| `POST /v1/sessions` (start a game)     | 23 ms | 30 ms     | 35 ms |
| `POST …/answer`                        | 19 ms | 25 ms     | 40 ms |
| `GET /v1/sessions/:id`                 | 15 ms | 19 ms     | 31 ms |
| `GET /v1/progress`                     | 13 ms | 17 ms     | 19 ms |
| `GET /v1/sessions/current?kind=REVIEW` | 14 ms | 17 ms     | 30 ms |
| **All of the above**                   | 15 ms | **25 ms** | 80 ms |

Totals: 1,909 requests in 3 minutes, 9.1 a second, none failed.

**Sign-in.** All 25 learners signed in within the same second, the worst case. The server took a median of 436 ms and a p95 of 463 ms.

**Headroom.** The same test with 50 learners for 2 minutes (17.8 requests a second, twice V22) gave the same p95 of 26 ms, with no failures. `GET /v1/me` rose to a p95 of 107 ms. It is each learner's first request after signing in, so most likely it waits behind the scrypt work of everyone signing in together.

**What this does not measure.**

- **The network.** Production adds the round trip between Thailand and Singapore and Neon's latency.
- **Neon.** The free plan's compute may be slower than a local database, and starts cold after a pause.

Check this from production's own logs: every API request is logged with its duration (`GET /v1/worlds 200 14ms`), so once learners use production, the slow ones can be found there.

### Running it again

You need k6, either installed (`winget install k6 --source winget`) or through Docker (`grafana/k6`), with the local database running (`pnpm infra:up`).

1. **Seed the learners.**
   ```bash
   pnpm --filter @languze/api load:seed
   ```
   This creates 25 verified learners, `learner-01@load.example.com` and on, each with a ready world of eight words. It writes only to a database on this machine.
2. **Start the API as the production build.** Use another port, so that `pnpm dev` can stay on 4001. Trust one proxy hop, so that each learner's `X-Forwarded-For` counts as its own address:
   ```bash
   pnpm --filter @languze/api build
   cd apps/api
   PORT=4011 WEB_ORIGIN=http://localhost:3003 AUTH_URL=http://localhost:4011 COOKIE_DOMAIN= TRUST_PROXY_HOPS=1 GEMINI_API_KEY= node --env-file-if-exists=.env dist/main.js
   ```
3. **Run k6.**
   - Installed:
     ```bash
     k6 run -e API_URL=http://localhost:4011 apps/api/load/learners.js
     ```
   - Through Docker, from the repository root (in Git Bash on Windows, put `MSYS_NO_PATHCONV=1` before `docker`, or the paths are rewritten):
     ```bash
     docker run --rm -v "$PWD/apps/api/load:/scripts" grafana/k6 run -e API_URL=http://host.docker.internal:4011 /scripts/learners.js
     ```
   - `-e LEARNERS=50 -e DURATION=2m` changes the load; seed as many learners first, with `load:seed -- --learners 50`.
4. **Remove the learners.**
   ```bash
   pnpm --filter @languze/api load:seed -- --clean
   ```

Run it again before a release that changes a query, an endpoint the journey uses, or the database.

## 2. NFR-003: the tutor

The AI evaluation measured the time to the tutor's first piece of reply: p95 4.8 s against the limit of 10 s, with Gemini 3.5 Flash-Lite on the free tier ([ADR-0004](../architecture/adr/0004-ai-provider.md#evaluation-results-2026-10-06)). A re-run on 2026-10-23, with the same model and tier, took p95 17.4 s ([ADR-0004](../architecture/adr/0004-ai-provider.md#tutor-re-run-2026-10-23-plain-text-replies)). Nothing in LanguZe had changed that adds time, and the slowest replies were the ones that look something up first, so the free tier's speed on the day is the likely cause. Production runs on a paid key, so NFR-003 is confirmed there.

`ai_calls` records each tutor reply as one row, timed from the request to the **end** of the stream. That makes it an upper bound: the first piece always arrives sooner. Once the tutor has answered at least 20 messages in production, some of them questions about the learner's own mistakes, which make the tutor look things up first, run this read-only query in Neon's SQL editor:

```sql
SELECT count(*) AS replies,
       round((percentile_cont(0.5) WITHIN GROUP (ORDER BY latency_ms) / 1000)::numeric, 1) AS median_s,
       round((percentile_cont(0.95) WITHIN GROUP (ORDER BY latency_ms) / 1000)::numeric, 1) AS p95_s,
       round((max(latency_ms) / 1000.0)::numeric, 1) AS slowest_s
FROM ai_calls
WHERE purpose = 'TUTOR' AND outcome = 'SUCCEEDED';
```

- **`p95_s` of 10 or less:** NFR-003 is met, since every reply had finished by then
- **More than 10:** the whole replies are too slow to prove it, though their first pieces may not be. Recording the first piece's time as well would settle it, which is a change to `ai_calls` and so a migration

**Result (2026-10-23): met.** 21 replies on the paid key, median 1.7 s, p95 2.0 s, the slowest 2.0 s. Those are whole replies, so the first piece came sooner than 2 s at the 95th percentile, well inside 10 s. The free tier's 17.4 s in the evaluation that day was the tier, not the tutor. Run the query again if the model, the tier or the tutor's tools change.

## 3. NFR-001: from upload to a ready world, in production

The time this requirement counts starts once the upload has finished, and that is exactly what the `analyses` table records. In the AI evaluation the two AI calls took p95 6.9 s together, so the 20 s limit leaves room for storage and the database. Production is still to be confirmed.

Once learners have made at least 20 worlds in production (your run-through and the closed test), run this read-only query in Neon's SQL editor:

```sql
SELECT count(*) AS worlds,
       round(percentile_cont(0.5) WITHIN GROUP (ORDER BY extract(epoch FROM finished_at - started_at))::numeric, 1) AS median_seconds,
       round(percentile_cont(0.95) WITHIN GROUP (ORDER BY extract(epoch FROM finished_at - started_at))::numeric, 1) AS p95_seconds
FROM analyses
WHERE status = 'SUCCEEDED';
```

NFR-001 holds when `p95_seconds` is 20 or less. Record the result here and in the launch checklist.
