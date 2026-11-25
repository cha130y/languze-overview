---
name: db-migration
description: Safely changes the LanguZe EF Core/PostgreSQL schema, creates and reviews migrations, updates data documentation, and verifies the result.
argument-hint: <schema-change>
---

# Database Migration Workflow

1. Read the entities in `apps/api-dotnet/LanguZe.Api/Data/Entities` and the relevant migrations in `Data/Migrations`.
2. Identify affected models, relations, constraints, indexes, and application queries.
3. Check whether the requested change can be made without schema modification.
4. If schema modification is required, change the entity and create a migration with `dotnet ef migrations add <Name> --project apps/api-dotnet/LanguZe.Api --output-dir Data/Migrations`.
5. Inspect generated SQL before applying it.
6. Identify destructive operations, backfill requirements, locking risks, and rollback considerations.
7. Update ERD/data documentation when the domain model changes.
8. Run `dotnet build LanguZe.slnx`, the unit tests (`MigrationTests` fails when the model changes without a migration), the integration tests, and `pnpm db:dotnet`.
9. Report exactly what the migration changes and what was verified.

Never fake a migration by editing generated SQL only or by manually changing a shared database.
