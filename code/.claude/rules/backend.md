---
paths:
  - "apps/api/**/*.{ts,tsx}"
---

# Backend Rules

- Keep controllers thin: transport concerns in controllers, business rules in services/use-cases.
- Validate request input at the HTTP boundary using the project's DTO/validation convention.
- Return explicit response DTOs/contracts; do not expose Prisma entities by accident.
- Keep transactions around multi-write business invariants.
- Do not broadcast Socket.IO/domain events before the transaction establishing the event has committed.
- Keep external providers behind adapters/services.
- Prefer domain errors mapped to stable API responses rather than leaking low-level exceptions.
- Add or update unit/integration tests for non-trivial business rules.
