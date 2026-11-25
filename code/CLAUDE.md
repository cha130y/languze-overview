# LanguZe — Claude Code

The project instructions are in `AGENTS.md`, shared with Codex. Edit them there, not here.

@AGENTS.md

## Claude Code only

- Skills in `.claude/skills/` are invoked as `/<name>` (for example `/commit`); project agents in `.claude/agents/` are listed by `/agents`.
- After changing a skill or an agent, run `pnpm ai:sync` so Codex gets the same version, and `pnpm ai:check` before committing.
- Claude Code's own memory may hold the user's preferences too; anything Codex also needs goes in `output/ai/CONTEXT.md` or `output/ai/LESSONS.md` as well (`AGENTS.md`, "Working with two assistants").
