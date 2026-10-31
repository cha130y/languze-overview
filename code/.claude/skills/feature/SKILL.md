---
name: feature
description: Implements a LanguZe feature end-to-end from requirement discovery through code, tests, verification, and documentation. Use for non-trivial feature work.
argument-hint: <requirement-id-or-feature>
---

# Feature Delivery Workflow

1. Read the relevant requirement, acceptance criteria, ADRs, process flow, and existing implementation before editing.
2. Inspect `git status --short`, repository structure, relevant tests, and Prisma schema when data is involved.
3. State affected domains, likely files, data/API/UI impact, verification plan, and documentation impact.
4. For cross-domain changes, consult the architect agent before implementation.
5. Implement a coherent vertical slice: contract -> backend -> persistence -> frontend -> tests.
6. Use existing patterns before introducing new abstractions.
7. Run focused tests first, then broader checks appropriate to the repository.
8. Review the final diff for unrelated changes, secrets, debug artifacts, and generated files.
9. Update durable docs when behavior, schema, flow, or architecture changed.
10. Report implemented behavior, files changed, checks actually run, and known limitations.

Do not commit or push automatically. Use `/commit` only after the user explicitly asks to ship the change.
