// PostToolUse hook: formats a C# file right after Claude writes or edits it, so the
// next read already has the repository's style and CI's format check stays green
// (ADR-0006, section 9). Whitespace only and one file at a time, so it stays quick;
// analyzers and the full `dotnet format --verify-no-changes` are CI's job.
//
// It never blocks: a missing SDK or a failed format simply leaves the file as written.

import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';

function changedFile() {
  try {
    const payload = JSON.parse(readFileSync(0, 'utf8'));
    return payload.tool_input?.file_path ?? payload.tool_response?.filePath;
  } catch {
    return undefined;
  }
}

const file = changedFile();
const root = process.env.CLAUDE_PROJECT_DIR ?? process.cwd();
const relative = file ? path.relative(root, path.resolve(file)) : '';

if (file?.endsWith('.cs') && !relative.startsWith('..')) {
  const args = ['format', 'whitespace', root, '--folder', '--include', relative];
  const run = (dotnet) =>
    spawnSync(dotnet, args, { stdio: 'ignore', timeout: 60_000 });
  // The SDK may be installed after the session started, before PATH caught up.
  const first = run('dotnet');
  if (first.error && process.platform === 'win32') {
    run('C:\\Program Files\\dotnet\\dotnet.exe');
  }
}
