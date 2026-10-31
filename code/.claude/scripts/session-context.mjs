import { execFileSync } from 'node:child_process'

const run = (args) => {
  try {
    return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()
  } catch {
    return ''
  }
}

const branch = run(['branch', '--show-current'])
const status = run(['status', '--short'])
const recent = run(['log', '--oneline', '-5'])

console.log(`LanguZe session context\nbranch: ${branch || '(detached/unavailable)'}\nworking tree: ${status ? 'has changes' : 'clean'}\nrecent commits:\n${recent || '(unavailable)'}`)
