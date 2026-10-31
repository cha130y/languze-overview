---
name: docs-sync
description: Synchronizes LanguZe requirements, architecture, flows, ERD, AI documentation, and traceability with implemented behavior.
argument-hint: [scope]
---

# Documentation Sync Workflow

1. Inspect the implementation and current docs.
2. Identify documentation that is stale because of the change.
3. Update only durable facts and decisions; do not document temporary implementation details.
4. Keep requirement IDs and acceptance criteria traceable to implementation/tests where the project uses them.
5. Use an ADR for significant architecture decisions rather than burying rationale in generic docs.
6. For AI behavior, update `docs/ai/` with provider assumptions, schemas, evaluation, and failure behavior where relevant.
7. Review the final diff to ensure docs describe code that actually exists.

Do not invent status or mark requirements complete without evidence.
