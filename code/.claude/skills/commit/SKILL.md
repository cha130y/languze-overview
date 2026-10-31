---
name: commit
description: Safely prepares and ships a LanguZe change by checking status, reviewing the diff, creating a Conventional Commit, pushing the branch, and preparing a PR URL. Never creates or merges the PR automatically.
disable-model-invocation: true
argument-hint: [requirement-id]
---

# LanguZe Shipping Workflow

This skill is confirmation-gated.

## 1. Preflight

Run:

```bash
git fetch --prune origin
git branch --show-current
git status --short
git diff
git diff --staged
```

Stop if the current branch is `main`, secrets appear in the diff, or unexplained user changes would be mixed into the commit.

Check whether the branch is ahead/behind `origin/main`.

## 2. Review

Determine the requirement ID from the argument or actual diff. Never invent an ID. Review requirement correctness, tests, documentation impact, migration safety, secrets/debug artifacts, and unrelated files. If multiple logical changes are mixed, propose separate commits.

## 3. Confirmation

Present the exact commit plan and wait for explicit user confirmation.

## 4. Commit

Stage explicit paths only. Do not use `git add .` or `git add -A`.

Use Conventional Commits: `<type>(<scope>): <imperative summary>`.

End the message with the `Co-Authored-By:` trailer that CLAUDE.md describes.

## 5. Re-check integration branch

Fetch again. If `origin/main` moved and the branch is now behind, stop and ask the user to reconcile before pushing.

## 6. Push

Push the current branch. Never force-push by default.

## 7. PR preparation

Check whether an open PR already exists for the branch. If it exists, return its URL. If none exists, prepare a GitHub compare URL targeting `main` with a concise title/body that ends with the line CLAUDE.md describes. Ask for explicit confirmation before any PR creation action.

This skill does not run `gh pr create` or `gh pr merge`.

## 8. Final report

Report commit hash, branch, push status, PR URL (existing or prepared), and exact verification performed.
