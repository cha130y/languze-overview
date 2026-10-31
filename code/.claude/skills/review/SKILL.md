---
name: review
description: Performs a structured read-only review of current LanguZe changes for correctness, security, architecture, tests, and scope.
argument-hint: [path-or-scope]
---

# Review Workflow

1. Inspect `git status --short` and `git diff`.
2. Identify the requirement and affected domains.
3. Review behavior before style.
4. Check authorization, validation, data integrity, AI output trust boundaries, and error paths.
5. Check test coverage and actual verification evidence.
6. Check architectural consistency and unnecessary dependencies.
7. Check secrets, generated files, debug code, and unrelated changes.
8. Report findings by severity: Blocker, High, Medium, Low, then optional suggestions.

Do not modify files during this skill. Do not report a finding without pointing to the relevant file/behavior.
