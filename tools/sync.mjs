// Refreshes code/ from the private LanguZe repository, as of a commit rather than its
// working tree, so unfinished work there can never come along. Run from this
// repository's root:
//   node tools/sync.mjs            # LanguZe's origin/main, fetched first
//   node tools/sync.mjs <ref>      # any commit, branch or tag LanguZe knows
//
// It copies only the files manifest.json lists that git tracks at that commit, so
// untracked or ignored files, such as environment files, cannot come along. It builds
// and validates a staged replacement, preserving code/ if the manifest, secret scan
// or README link check fails. Nothing is committed: review the diff.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';

const manifest = JSON.parse(readFileSync('tools/manifest.json', 'utf8'));
const source = resolve(manifest.source);
const git = (...args) => execFileSync('git', ['-C', source, ...args], { maxBuffer: 64 * 1024 * 1024 });

const ref = process.argv[2] ?? 'origin/main';
if (!process.argv[2]) {
  git('fetch', '--quiet', 'origin');
}
const commit = git('rev-parse', '--short', `${ref}^{commit}`).toString().trim();
const tracked = git('ls-tree', '-r', '--name-only', commit).toString().split('\n').filter(Boolean);

const matchesEntry = (entry) => entry.endsWith('/')
  ? tracked.filter((file) => file.startsWith(entry))
  : tracked.filter((file) => file === entry);
const missing = manifest.include.filter((entry) => matchesEntry(entry).length === 0);
if (missing.length > 0) {
  console.error(`Nothing tracked at ${commit} matches these manifest entries:`);
  for (const entry of missing) console.error(`- ${entry}`);
  console.error('Update tools/manifest.json. The existing code/ snapshot was not changed.');
  process.exit(1);
}

// Build and validate a complete replacement beside code/. Do not touch the current
// snapshot until the manifest, secret scan and README links have all passed.
const stage = mkdtempSync(join(process.cwd(), '.code-sync-'));
const copied = [];
try {
  for (const entry of manifest.include) {
    for (const file of matchesEntry(entry)) {
      mkdirSync(dirname(join(stage, file)), { recursive: true });
      writeFileSync(join(stage, file), git('show', `${commit}:${file}`));
      copied.push(file);
    }
  }

// What a secret or a personal address looks like. A match stops the sync: look at the
// file, and either leave it out of the manifest or add the value to the allowed list
// below if it is a test value.
const suspicious = [
  [/AIza[0-9A-Za-z_-]{30,}/, 'a Google API key'],
  [/\bsk-[A-Za-z0-9_-]{20,}/, 'an API secret key'],
  [/\bre_[A-Za-z0-9]{20,}/, 'a Resend API key'],
  [/\bgh[pousr]_[A-Za-z0-9]{30,}/, 'a GitHub token'],
  [/-----BEGIN [A-Z ]*PRIVATE KEY-----/, 'a private key'],
  [/postgres(?:ql)?:\/\/[^\s'"`]*:[^\s'"`@]+@(?!localhost|127\.0\.0\.1)[^\s'"`]+/, 'a database address with a password'],
  [/[A-Za-z0-9._%+-]+@(?:gmail|hotmail|outlook|yahoo|icloud)\.[a-z.]+/i, 'a personal email address'],
];
const textExtensions = new Set(['.cs', '.ts', '.tsx', '.mjs', '.js', '.json', '.md', '.yml', '.yaml', '.props', '.csproj', '.sql', '.txt']);
const findings = [];
for (const file of copied) {
  if (!textExtensions.has(extname(file))) continue;
  const text = readFileSync(join(stage, file), 'utf8');
  for (const [pattern, what] of suspicious) {
    if (pattern.test(text)) findings.push(`${file}: ${what}`);
  }
}

// Every local link in README.md must reach a file in this repository.
const readme = readFileSync('README.md', 'utf8');
const broken = [...readme.matchAll(/\]\(([^)#\s]+)(?:#[^)]*)?\)/g)]
  .map((match) => match[1])
  .filter((link) => {
    if (/^[a-z]+:/i.test(link)) return false;
    const target = link.startsWith('code/') ? join(stage, link.slice('code/'.length)) : link;
    return !existsSync(target);
  });

console.log(`Copied ${copied.length} files from LanguZe at ${commit} into code/`);
if (findings.length > 0 || broken.length > 0) {
  for (const finding of findings) console.error(`Looks like a secret: ${finding}`);
  for (const link of broken) console.error(`Broken link in README.md: ${link}`);
  console.error('The existing code/ snapshot was not changed.');
  process.exitCode = 1;
} else {
  const backup = `.code-backup-${process.pid}`;
  if (existsSync(backup)) throw new Error(`Refusing to replace existing backup ${backup}`);
  const hadCode = existsSync('code');
  if (hadCode) renameSync('code', backup);
  try {
    renameSync(stage, 'code');
  } catch (error) {
    if (hadCode) renameSync(backup, 'code');
    throw error;
  }
  if (hadCode) rmSync(backup, { recursive: true, force: true });
  console.log('No secrets found, and every link in README.md works. Review the diff, then commit.');
}
} finally {
  if (existsSync(stage)) rmSync(stage, { recursive: true, force: true });
}
