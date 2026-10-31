# Git Rules

- Inspect `git status`, branch, and diff before modifying or staging files.
- Do not use `git add .` or `git add -A` in the shipping workflow; stage explicit paths.
- Never commit secrets or generated local environment files.
- Do not rewrite or discard user changes that predate the current task.
- Do not force-push by default.
- Do not merge or create a PR without the user's explicit confirmation in the shipping workflow.
