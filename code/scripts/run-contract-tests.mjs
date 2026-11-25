// Runs the API contract tests (tests/api-contract) against the API this script starts
// (ADR-0006, section 3), then compares its OpenAPI document with
// tests/api-contract/openapi.json, the contract the web app relies on. Changing the API's
// surface means changing that file on purpose.
//
// The API gets the fake AI provider and Maildev whatever its environment says, and one
// trusted proxy hop, so each test can stand for its own caller through X-Forwarded-For and
// the per-address rate limits never mix two tests together. The database, Maildev and
// storage come from `pnpm infra:up`.
//
// It first brings the API's database, `languze_dotnet`, up to date
// (scripts/prepare-dotnet-database.mjs). The tests are told which database it is through
// CONTRACT_DATABASE_URL, for what only the database can set up, such as an admin.
//
// Usage: node scripts/run-contract-tests.mjs [--no-start] [-- <dotnet test arguments>]
//   --no-start       test an API that is already running at CONTRACT_API_URL, whose
//                    database CONTRACT_DATABASE_URL names
// `--target dotnet`, from before the NestJS API was removed, is still accepted.

import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { diffOpenApi } from './openapi-diff.mjs';
import {
  dotnetDatabaseUrl,
  prepareDotnetDatabase,
} from './prepare-dotnet-database.mjs';

const API = {
  url: 'http://localhost:4001',
  command: 'dotnet',
  args: [
    'run',
    '--project',
    'apps/api-dotnet/LanguZe.Api',
    '--no-launch-profile',
  ],
  env: {
    ASPNETCORE_ENVIRONMENT: 'Development',
    DATABASE_URL: dotnetDatabaseUrl(),
  },
};

const args = process.argv.slice(2);
const split = args.indexOf('--');
const ownArgs = split === -1 ? args : args.slice(0, split);
const dotnetArgs = split === -1 ? [] : args.slice(split + 1);
const startApi = !ownArgs.includes('--no-start');
const targetName = ownArgs.includes('--target')
  ? ownArgs[ownArgs.indexOf('--target') + 1]
  : 'dotnet';

const API_URL = process.env.CONTRACT_API_URL ?? API.url;
const WEB_ORIGIN = process.env.CONTRACT_WEB_ORIGIN ?? 'http://localhost:3003';
const PROJECT = 'tests/api-contract';
const CONTRACT = 'tests/api-contract/openapi.json';

// The .NET CLI and the test platform both send usage data unless told not to.
const NO_TELEMETRY = {
  DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  TESTINGPLATFORM_TELEMETRY_OPTOUT: '1',
};

const API_ENV = {
  PORT: new URL(API_URL).port,
  WEB_ORIGIN,
  AUTH_URL: API_URL,
  COOKIE_DOMAIN: '',
  TRUST_PROXY_HOPS: '1',
  // The default limit RateLimitTests counts to, whatever the environment says.
  RATE_LIMIT_PER_MINUTE: '120',
  ORIGIN_SECRET: '',
  GEMINI_API_KEY: '',
  AI_FAKE_BEHAVIOUR: 'ALLOWED',
  AI_FAKE_DELAY_MS: '0',
  RESEND_API_KEY: '',
  MAIL_HOST: 'localhost',
  MAIL_PORT: process.env.MAILDEV_SMTP_PORT ?? '1026',
  MAIL_USER: '',
  MAIL_PASSWORD: '',
  // Each provider gets a client of its own, so its sign-in can start, whatever the
  // environment holds; AuthProviderTests names them.
  GOOGLE_CLIENT_ID: 'contract-google-client-id',
  GOOGLE_CLIENT_SECRET: 'contract-google-client-secret',
  LINE_CLIENT_ID: 'contract-line-client-id',
  LINE_CLIENT_SECRET: 'contract-line-client-secret',
  FACEBOOK_CLIENT_ID: 'contract-facebook-client-id',
  FACEBOOK_CLIENT_SECRET: 'contract-facebook-client-secret',
};

async function healthy() {
  try {
    return (await fetch(`${API_URL}/health`)).ok;
  } catch {
    return false;
  }
}

function run(command, commandArgs, options = {}) {
  return spawn(command, commandArgs, {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    ...options,
  });
}

function exitCodeOf(child) {
  return new Promise((resolve) =>
    child.on('exit', (code) => resolve(code ?? 1)),
  );
}

/** Ends the API and everything it started, which `dotnet run` spreads over processes. */
function stop(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === 'win32') {
    spawn('taskkill', ['/pid', String(child.pid), '/T', '/F'], {
      stdio: 'ignore',
    });
  } else {
    process.kill(-child.pid, 'SIGTERM');
  }
}

async function waitForApi(child, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (child.exitCode !== null)
      throw new Error('The API exited before it was ready.');
    if (await healthy()) return;
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(
    `The API did not answer ${API_URL}/health within ${timeoutMs / 1000} s.`,
  );
}

/** Compares the API's OpenAPI document with the contract, on every path. */
async function compareOpenApi() {
  const base = JSON.parse(readFileSync(CONTRACT, 'utf8'));
  const response = await fetch(`${API_URL}/openapi/v1.json`);
  if (!response.ok) {
    console.error(
      `The API served no OpenAPI document at ${API_URL}/openapi/v1.json (${response.status}); it does in Development.`,
    );
    return 1;
  }
  const diffs = diffOpenApi(base, await response.json());
  console.log(`\nOpenAPI comparison with ${CONTRACT}:`);
  for (const line of diffs) console.log(`- ${line}`);
  console.log(
    diffs.length === 0 ? 'No differences.' : `${diffs.length} difference(s).`,
  );
  return diffs.length === 0 ? 0 : 1;
}

async function main() {
  if (targetName !== 'dotnet') {
    console.error(
      `Unknown target "${targetName}": the NestJS API was removed, and only the ASP.NET Core API is tested.`,
    );
    return 2;
  }

  let api;
  if (startApi) {
    if (await healthy()) {
      console.error(
        `Something already answers at ${API_URL}. Stop it, or pass --no-start to test it as it is.`,
      );
      return 1;
    }
    prepareDotnetDatabase(API.env.DATABASE_URL);
    api = run(API.command, API.args, {
      env: { ...process.env, ...API_ENV, ...NO_TELEMETRY, ...API.env },
      detached: process.platform !== 'win32',
    });
    process.on('exit', () => stop(api));
    await waitForApi(api, 180_000);
  }

  try {
    const tests = run(
      'dotnet',
      ['test', '--project', PROJECT, '--', ...dotnetArgs],
      {
        env: {
          ...process.env,
          ...NO_TELEMETRY,
          CONTRACT_API_URL: API_URL,
          CONTRACT_WEB_ORIGIN: WEB_ORIGIN,
          CONTRACT_DATABASE_URL:
            process.env.CONTRACT_DATABASE_URL ??
            (startApi ? API.env.DATABASE_URL : ''),
        },
      },
    );
    const testsCode = await exitCodeOf(tests);
    const openApiCode = await compareOpenApi();
    return testsCode || openApiCode;
  } finally {
    stop(api);
  }
}

main().then(
  (code) => {
    process.exitCode = code;
  },
  (error) => {
    console.error(error.message);
    process.exitCode = 1;
  },
);
