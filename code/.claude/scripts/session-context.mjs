// SessionStart hook, for Claude Code (.claude/settings.json) and Codex (.codex/hooks.json): what it prints becomes
// context at the start of every session (new, resumed, cleared or compacted). It shows the git state and the newest
// handoff entry from output/ai/HANDOFF.md, so an assistant taking over from the other one starts from the written
// state, not from a guess (AGENTS.md, "Working with two assistants"). Never fails: missing git or files print less.
import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const MAX_LINES = 30

const run = (args) => {
  try {
    return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()
  } catch {
    return ''
  }
}

/** The newest entry of HANDOFF.md: from its first `## ` heading to the next one, cut to MAX_LINES. */
export function latestHandoff(text) {
  const lines = text.split(/\r?\n/)
  const start = lines.findIndex((l) => l.startsWith('## '))
  if (start < 0) return ''
  let end = lines.findIndex((l, i) => i > start && l.startsWith('## '))
  if (end < 0) end = lines.length
  const entry = lines.slice(start, end)
  while (entry.length && !entry[entry.length - 1].trim()) entry.pop()
  return entry.length > MAX_LINES ? [...entry.slice(0, MAX_LINES), `… (${entry.length - MAX_LINES} more lines in output/ai/HANDOFF.md)`].join('\n') : entry.join('\n')
}

function main() {
  const root = process.env.CLAUDE_PROJECT_DIR || process.cwd()
  const branch = run(['branch', '--show-current'])
  const status = run(['status', '--short'])
  const recent = run(['log', '--oneline', '-5'])

  let out = `LanguZe session context\nbranch: ${branch || '(detached/unavailable)'}\nworking tree: ${status ? 'has changes' : 'clean'}\nrecent commits:\n${recent || '(unavailable)'}`

  const live = join(root, 'output', 'ai', 'LIVE.md')
  if (existsSync(live)) {
    let note = ''
    try { note = readFileSync(live, 'utf8').trim() } catch {}
    if (note) out += `

${note}
If the last activity was by the other assistant and newer than the handoff entry below, its work may be unfinished and unrecorded: look at the uncommitted files and ask the user before continuing it.`
  }

  const handoff = join(root, 'output', 'ai', 'HANDOFF.md')
  if (existsSync(handoff)) {
    let entry = ''
    try { entry = latestHandoff(readFileSync(handoff, 'utf8')) } catch {}
    if (entry) out += `\n\nNewest handoff entry (output/ai/HANDOFF.md). Check it against git before relying on it; read output/ai/LESSONS.md before working; write a new entry when you finish a turn of work:\n${entry}`
  }

  console.log(out)
}

// run when called as the hook, not when a test imports latestHandoff
if (process.argv[1] && resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase()) main()
