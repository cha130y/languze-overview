// Stop hook, for Claude Code (.claude/settings.json) and Codex (.codex/hooks.json): at the end of every turn it writes
// output/ai/LIVE.md, a few facts about the last turn (which assistant, when, branch, uncommitted files), so that if the
// user switches without asking for a handoff, the other assistant still learns that work was left in progress and
// where. The SessionStart hook (session-context.mjs) prints it. Facts only: the plan and the reasons belong in
// HANDOFF.md ("ส่งงาน"); no conversation text is kept (security rules: no raw conversations in logs). Never blocks or continues a turn, and prints nothing (Codex's Stop expects JSON or nothing).
//
//   node .claude/scripts/record-activity.mjs <claude|codex>
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const MAX_FILES = 25

const run = (args, cwd) => {
  try {
    return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trimEnd()   // not trim(): status lines start with a space
  } catch {
    return ''
  }
}

/** The text of LIVE.md for one turn. */
export function liveNote({ who, when, branch, head, status }) {
  const files = status ? status.split(/\r?\n/) : []
  const lines = [
    '# Last activity (written by the Stop hook at the end of every turn; facts only, see HANDOFF.md for the plan)',
    '',
    `- by: ${who}`,
    `- at: ${when}`,
    `- branch: ${branch || '(detached/unavailable)'}`,
    `- HEAD: ${head || '(unavailable)'}`,
    `- uncommitted files: ${files.length ? files.length : 'none'}`,
    ...files.slice(0, MAX_FILES).map((f) => `  - \`${f}\``),
    ...(files.length > MAX_FILES ? [`  - … and ${files.length - MAX_FILES} more`] : []),
  ]
  return lines.join('\n') + '\n'
}

function main() {
  const who = process.argv[2] === 'codex' ? 'Codex' : 'Claude Code'
  let payload = {}
  try { payload = JSON.parse(readFileSync(0, 'utf8') || '{}') } catch {}
  const root = process.env.CLAUDE_PROJECT_DIR || payload.cwd || process.cwd()
  const dir = join(root, 'output', 'ai')
  if (!existsSync(join(root, 'AGENTS.md'))) return   // not this repository
  const note = liveNote({
    who,
    when: new Date().toLocaleString('sv-SE', { timeZone: 'Asia/Bangkok' }) + ' (Bangkok)',
    branch: run(['branch', '--show-current'], root),
    head: run(['log', '-1', '--format=%h %s'], root),
    status: run(['status', '--short'], root),
  })
  try { mkdirSync(dir, { recursive: true }); writeFileSync(join(dir, 'LIVE.md'), note) } catch {}
}

if (process.argv[1] && resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase()) main()
