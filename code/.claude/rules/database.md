---
paths:
  - "apps/api-dotnet/LanguZe.Api/Data/**/*"
  - "docs/architecture/**/*.{md,dbml}"
---

# Database Rules

- Treat the EF Core model and migrations (`apps/api-dotnet/LanguZe.Api/Data`) as the database source of truth.
- Never manually patch a shared database as a substitute for a migration.
- Every schema change must have a migration reviewed before application.
- Preserve foreign keys, uniqueness, nullability, and indexes intentionally.
- Use transactions for invariants spanning multiple writes.
- Review query shape and indexes when introducing high-volume learning, game, leaderboard, or event data.
- If the domain model changes, update the ERD/data documentation when practical.
- Do not introduce MongoDB or a separate vector database without a documented requirement and ADR.
