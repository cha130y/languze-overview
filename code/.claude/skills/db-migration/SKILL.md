---
name: db-migration
description: Safely changes the LanguZe Prisma/PostgreSQL schema, creates and reviews migrations, updates data documentation, and verifies the result.
argument-hint: <schema-change>
---

# Database Migration Workflow

1. Read the current Prisma schema and relevant migrations.
2. Identify affected models, relations, constraints, indexes, and application queries.
3. Check whether the requested change can be made without schema modification.
4. If schema modification is required, update Prisma schema and create a migration using the repository's normal command.
5. Inspect generated SQL before applying it.
6. Identify destructive operations, backfill requirements, locking risks, and rollback considerations.
7. Update ERD/data documentation when the domain model changes.
8. Run Prisma validation/generation and relevant application tests.
9. Report exactly what the migration changes and what was verified.

Never fake a migration by editing generated SQL only or by manually changing a shared database.
