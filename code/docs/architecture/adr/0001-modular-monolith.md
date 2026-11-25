# ADR-0001: Start as a modular monolith

- **Status:** Accepted
- **Date:** 2026-09-17; the backend has been ASP.NET Core since 2026-11-06 ([ADR-0006](0006-aspnet-core-api.md)), and the decision stands

## Context

LanguZe is a production product built by a solo developer. The product spans several domains (auth, users, worlds/images, vocabulary, learning/mastery, games, progress/achievements, notifications, AI/tutor, realtime), and the long-term roadmap includes realtime play, event-driven processing, RAG, and possibly separate services.

Splitting into microservices early would add deployment, networking, observability, and data-consistency work before any domain has a scaling or ownership reason to be separate.

## Decision

Build the backend as a single application organized as a modular monolith, first in NestJS and since 2026-11-06 in ASP.NET Core (`apps/api-dotnet`, ADR-0006):

- One module per domain, with explicit boundaries and no circular dependencies between domains.
- Endpoints stay thin; business rules live in handlers and the module's domain; persistence stays behind the data-access boundary.
- One PostgreSQL database, with the migrations as the source of truth: Prisma's at first, EF Core's since the cutover.
- The frontend (`apps/web`) is a separate Next.js application that talks to the API over HTTP.
- Shared packages (`packages/*`) are created only for stable cross-boundary contracts or configuration.

## Consequences

- Lower operational complexity: one API deployable, one database, simple local development with Docker Compose.
- Domain boundaries are not enforced by network separation. In the ASP.NET Core API, architecture tests fail the build when a module reaches past another's contracts (ADR-0006, section 1); a cycle between two modules' contracts is still left to review ([architecture overview](../overview.md#5-api-modules)).
- A module can be extracted into a service later when scale, deployment, ownership, or reliability requirements justify it; that extraction requires its own ADR.
- Infrastructure such as Redis, RabbitMQ, and Socket.IO is added only when a concrete feature needs it.
