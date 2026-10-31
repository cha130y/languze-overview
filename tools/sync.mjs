// Copies the files in manifest.json from the private LanguZe repository into this one.
// Only files git tracks there are copied, so untracked or ignored files, such as
// environment files, can never come along. Run from this repository's root:
//   node tools/sync.mjs
import { execFileSync } from 'node:child_process';
import { copyFileSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

const manifest = JSON.parse(readFileSync('tools/manifest.json', 'utf8'));
const source = resolve(manifest.source);
const tracked = execFileSync('git', ['-C', source, 'ls-files'], { encoding: 'utf8' }).split('\n').filter(Boolean);

const copied = [];
for (const entry of manifest.include) {
  const matches = entry.endsWith('/') ? tracked.filter((file) => file.startsWith(entry)) : tracked.filter((file) => file === entry);
  if (matches.length === 0) {
    throw new Error(`Nothing tracked matches ${entry}`);
  }
  for (const file of matches) {
    mkdirSync(dirname(join('code', file)), { recursive: true });
    copyFileSync(join(source, file), join('code', file));
    copied.push(file);
  }
}

console.log(`Copied ${copied.length} files into code/`);
