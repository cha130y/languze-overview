# ADR-0005: PostgreSQL as the only database, pgvector when RAG arrives

- **Status:** Accepted
- **Date:** 2026-09-19
- **Related:** [ADR-0001](0001-modular-monolith.md), [ADR-0002](0002-toolchain-baseline.md), [architecture overview](../overview.md), [data model](../data-model.md); PRD roadmap (RAG tutor)

## Context

LanguZe's data is relational: accounts, worlds, words, occurrences, attempts, and mastery reference each other, and several business rules need transactions across tables (an attempt with its mastery and XP; an admin action with its audit entry). Daily limits need locking, and some rules need partial unique indexes, check constraints, and a trigger ([data model](../data-model.md)).

The roadmap adds a tutor grounded in the learner's full history through retrieval-augmented generation (RAG), which needs vector search. Later features such as leaderboards and multiplayer may need fast shared state. The project rules say not to add MongoDB or a separate vector database without a documented requirement and an ADR, and to add infrastructure only for a concrete need.

## Options considered

| Option                                | For                                                                                                                                                        | Against                                                               |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| **PostgreSQL only, pgvector later**   | Transactions, constraints, and locks for the business rules; vector search added in the same database with an extension; one system to back up and operate | Vector search at very large scale is slower than in dedicated systems |
| PostgreSQL plus a vector database now | Purpose-built vector search                                                                                                                                | A second system to run and pay for before any feature needs it        |
| A document database (MongoDB)         | Flexible documents                                                                                                                                         | Weak fit for relational rules and cross-table transactions            |
| PostgreSQL plus Redis now             | Fast counters and shared state                                                                                                                             | Not needed with one API instance (A2); adds cost and operations       |

## Decision

- **PostgreSQL 18 is LanguZe's only database** in Release 1.0, used through Prisma 7 with the schema and migrations in `apps/api/prisma` (ADR-0002).
- Features that Prisma cannot express, such as check constraints, partial unique indexes, the audit-log trigger, and transaction-scoped advisory locks, are written as SQL in migrations or queries and listed in the data model.
- **pgvector** is already available in the database image, but the extension is enabled by a migration only when the RAG tutor is built, with its own requirement and design. A separate vector database needs a new ADR that shows pgvector is not enough.
- Redis, a job queue, or another store is added only for a need listed in section 8 of the architecture overview, with its own ADR.

## Consequences

- Business rules are protected by the database itself: transactions, foreign keys, unique and check constraints, and the audit trigger.
- One database to back up, restore, and monitor. The production host must provide PostgreSQL 18 with the pgvector extension available, automated backups, and point-in-time recovery; a restore is tested before launch. The host is chosen in the deployment document.
- Raw SQL in migrations must be reviewed with the same care as Prisma changes, and the data model must list it, because it does not appear in the Prisma schema.
- Moving to more API instances later changes only the locking and rate-limit parts (architecture overview, section 8), not the database choice.
