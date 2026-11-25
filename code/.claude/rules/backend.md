---
paths:
  - "apps/api-dotnet/**/*.cs"
---

# Backend Rules

- Keep endpoints thin: transport concerns in endpoints, business rules in handlers and the module's Domain (ADR-0006, section 1).
- Validate request input at the HTTP boundary using the project's request-record validation (`Platform/Validation`).
- Return explicit response DTOs/contracts; do not expose EF Core entities by accident.
- Keep transactions around multi-write business invariants.
- Do not broadcast Socket.IO/domain events before the transaction establishing the event has committed.
- Keep external providers behind adapters/services.
- Prefer domain errors mapped to stable API responses rather than leaking low-level exceptions.
- Add or update unit/integration tests for non-trivial business rules.
