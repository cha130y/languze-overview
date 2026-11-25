# LanguZe Claude Code Kit

This kit refactors the CBeave/BidNest style of `CLAUDE.md + command/SKILL` into a layered Claude Code setup for LanguZe.

## Layout

- `AGENTS.md` — the project constitution, shared by Claude Code and Codex; keep it concise (Codex reads at most 32 KiB).
- `CLAUDE.md` — imports `AGENTS.md` (`@AGENTS.md`) and adds the few Claude-only notes.
- `.agents/skills/`, `.codex/agents/` — Codex's copies of the skills and agents below, written by `pnpm ai:sync`; never edit them (`pnpm ai:check` fails when they drift).
- `output/ai/` (git-ignored) — the handoff, lessons and context both assistants read when the user switches between them.
- `.claude/rules/` — durable domain rules; path-scoped rules load only when relevant.
- `.claude/skills/` — repeatable workflows loaded on demand.
- `.claude/agents/` — specialist subagents for architecture, backend, frontend, database, AI, and final review.
- `.claude/settings.json` — shared permissions and a small SessionStart hook.
- `.claude/scripts/` — deterministic helper scripts.

## Install

The kit is installed at the repository root (`CLAUDE.md` + `.claude/`), and its `.gitignore.snippet` has been merged into the root `.gitignore`.

Do not commit `.claude/settings.local.json` or `CLAUDE.local.md`.

## First session checklist

1. Start Claude Code from the repository root.
2. Run `/status` and confirm the project settings are loaded.
3. Run `/context` and confirm `CLAUDE.md` is loaded.
4. Review `/agents` and confirm the project agents are discovered.
5. Test `/feature`, `/review`, `/debug`, and `/commit` on a small safe change.
6. Run `claude doctor` if configuration is rejected.

## Recommended usage

- `/feature <requirement>` — end-to-end implementation
- `/ai-feature <feature>` — AI-specific implementation/evaluation
- `/review` — read-only code review
- `/debug <problem>` — systematic debugging
- `/db-migration <change>` — Prisma/PostgreSQL schema change
- `/docs-sync` — synchronize docs with implementation
- `/commit <requirement-id>` — confirmation-gated shipping workflow

## Design principle

Do not put every instruction into `CLAUDE.md`. Keep always-needed facts there, use path-scoped rules for domain constraints, skills for repeatable workflows, agents for specialist context, hooks/settings for deterministic controls, and auto memory for learned project-specific context.
