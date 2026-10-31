# Architecture Rules

- Prefer a Modular Monolith until a real scaling/deployment/ownership boundary exists.
- Keep domain boundaries explicit even inside one deployable application.
- Introduce Redis, RabbitMQ, Socket.IO, RAG, or separate services only when a concrete requirement benefits from it.
- Avoid circular dependencies between domains.
- Shared packages must contain stable cross-boundary contracts/configuration, not arbitrary business logic moved out of convenience.
- Significant architectural changes require an ADR before or with implementation.
