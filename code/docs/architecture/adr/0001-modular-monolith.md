# ADR-0001: Start as a modular monolith

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

LanguZe is a production product built by a solo developer. The product spans several domains (auth, users, worlds/images, vocabulary, learning/mastery, games, progress/achievements, notifications, AI/tutor, realtime), and the long-term roadmap includes realtime play, event-driven processing, RAG, and possibly separate services.

Splitting into microservices early would add deployment, networking, observability, and data-consistency work before any domain has a scaling or ownership reason to be separate.

## Decision

Build the backend as a single NestJS application (`apps/api`) organized as a modular monolith:

- One NestJS module per domain, with explicit boundaries and no circular dependencies between domains.
- Controllers stay thin; business rules live in services/use-cases; persistence stays behind the data-access boundary.
- One PostgreSQL database, with Prisma schema + migrations as the source of truth.
- The frontend (`apps/web`) is a separate Next.js application that talks to the API over HTTP.
- Shared packages (`packages/*`) are created only for stable cross-boundary contracts or configuration.

## Consequences

- Lower operational complexity: one API deployable, one database, simple local development with Docker Compose.
- Domain boundaries must be enforced by convention and review rather than by network separation.
- A module can be extracted into a service later when scale, deployment, ownership, or reliability requirements justify it; that extraction requires its own ADR.
- Infrastructure such as Redis, RabbitMQ, and Socket.IO is added only when a concrete feature needs it.
