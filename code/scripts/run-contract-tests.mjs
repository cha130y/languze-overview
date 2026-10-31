// Runs the API contract tests (tests/api-contract) against an API this script starts:
// the NestJS API, or with `--target dotnet` its ASP.NET Core port (ADR-0006, section 3).
//
// The API gets the fake AI provider and Maildev whatever its .env says, and one trusted
// proxy hop, so each test can stand for its own caller through X-Forwarded-For and the
// per-address rate limits never mix two tests together. The database, Maildev and
// storage come from `pnpm infra:up`.
//
// Against .NET it first brings the API's own database, `languze_dotnet`, up to date
// (scripts/prepare-dotnet-database.mjs), runs only the slices ported so far, then
// compares the .NET API's OpenAPI document with apps/api/openapi.json on their paths.
//
// Usage: node scripts/run-contract-tests.mjs [--target nestjs|dotnet] [--no-start] [-- <dotnet test arguments>]
//   --target dotnet  the ASP.NET Core API in apps/api-dotnet, on port 4002
//   --no-start       test an API that is already running at CONTRACT_API_URL

import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { diffOpenApi } from './openapi-diff.mjs';
import {
  dotnetDatabaseUrl,
  prepareDotnetDatabase,
} from './prepare-dotnet-database.mjs';

/**
 * The slices whose .NET implementation is done, by the `Slice` trait of their contract
 * tests, with what the OpenAPI comparison checks for each: a path prefix such as
 * `/health`, or one operation such as `GET /v1/me` when other methods or longer paths
 * under it belong to slices not ported yet. `/port-slice` adds a slice here once its
 * contract tests pass against the .NET API.
 */
const PORTED_TO_DOTNET = {
  Platform: ['/health'],
  Auth: [
    'POST /v1/auth/sign-up',
    'POST /v1/auth/sign-in',
    'POST /v1/auth/sign-out',
    'GET /v1/me',
    'POST /v1/me/terms',
    'POST /v1/me/terms/decline',
    'POST /v1/auth/send-verification-email',
    'POST /v1/auth/verify-email',
    'POST /v1/auth/request-password-reset',
    'POST /v1/auth/reset-password',
    'GET /v1/auth/providers',
  ],
  Users: ['DELETE /v1/me'],
  Worlds: [
    'GET /v1/worlds',
    'POST /v1/worlds',
    'GET /v1/worlds/{worldId}',
    'GET /v1/worlds/{worldId}/status',
    'PATCH /v1/worlds/{worldId}',
    'DELETE /v1/worlds/{worldId}',
    'POST /v1/worlds/{worldId}/retry',
    'DELETE /v1/worlds/{worldId}/words/{occurrenceId}',
  ],
};

const TARGETS = {
  nestjs: {
    url: 'http://localhost:4001',
    command: 'pnpm',
    args: ['--filter', '@languze/api', 'start'],
    env: {},
  },
  dotnet: {
    url: 'http://localhost:4002',
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
  },
};

const args = process.argv.slice(2);
const split = args.indexOf('--');
const ownArgs = split === -1 ? args : args.slice(0, split);
const dotnetArgs = split === -1 ? [] : args.slice(split + 1);
const startApi = !ownArgs.includes('--no-start');
const targetName = ownArgs[ownArgs.indexOf('--target') + 1] ?? 'nestjs';
const target = ownArgs.includes('--target')
  ? TARGETS[targetName]
  : TARGETS.nestjs;

const API_URL = process.env.CONTRACT_API_URL ?? target?.url;
const WEB_ORIGIN = process.env.CONTRACT_WEB_ORIGIN ?? 'http://localhost:3003';
const PROJECT = 'tests/api-contract';

// The .NET CLI and the test platform both send usage data unless told not to.
const NO_TELEMETRY = {
  DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  TESTINGPLATFORM_TELEMETRY_OPTOUT: '1',
};

const API_ENV = {
  PORT: API_URL ? new URL(API_URL).port : '',
  WEB_ORIGIN,
  AUTH_URL: API_URL,
  COOKIE_DOMAIN: '',
  TRUST_PROXY_HOPS: '1',
  // The default limit RateLimitTests counts to, whatever the .env says.
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
  // .env holds; AuthProviderTests names them.
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

/** Ends the API and everything it started, which `nest start` spreads over processes. */
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

/** Compares the .NET API's OpenAPI document with NestJS's on the ported slices' paths and operations. */
async function compareOpenApi() {
  const scope = Object.values(PORTED_TO_DOTNET).flat();
  if (scope.length === 0) return 0;
  const operations = scope.filter((entry) => entry.includes(' '));
  const pathPrefixes = scope.filter((entry) => !entry.includes(' '));

  const base = JSON.parse(readFileSync('apps/api/openapi.json', 'utf8'));
  const response = await fetch(`${API_URL}/openapi/v1.json`);
  if (!response.ok) {
    console.error(
      `The .NET API served no OpenAPI document at ${API_URL}/openapi/v1.json (${response.status}); it does in Development.`,
    );
    return 1;
  }
  const diffs = diffOpenApi(base, await response.json(), {
    pathPrefixes,
    operations,
  });
  console.log(`\nOpenAPI comparison on ${scope.join(', ')}:`);
  for (const line of diffs) console.log(`- ${line}`);
  console.log(
    diffs.length === 0 ? 'No differences.' : `${diffs.length} difference(s).`,
  );
  return diffs.length === 0 ? 0 : 1;
}

async function main() {
  if (!target) {
    console.error(`Unknown target "${targetName}": use nestjs or dotnet.`);
    return 2;
  }
  const dotnet = target === TARGETS.dotnet;

  let api;
  if (startApi) {
    if (await healthy()) {
      console.error(
        `Something already answers at ${API_URL}. Stop it, or pass --no-start to test it as it is.`,
      );
      return 1;
    }
    if (dotnet) prepareDotnetDatabase(target.env.DATABASE_URL);
    api = run(target.command, target.args, {
      env: { ...process.env, ...API_ENV, ...NO_TELEMETRY, ...target.env },
      detached: process.platform !== 'win32',
    });
    process.on('exit', () => stop(api));
    await waitForApi(api, 180_000);
  }

  try {
    const sliceFilters = dotnet
      ? Object.keys(PORTED_TO_DOTNET).flatMap((slice) => [
          '--filter-trait',
          `Slice=${slice}`,
        ])
      : [];
    const tests = run(
      'dotnet',
      ['test', '--project', PROJECT, '--', ...sliceFilters, ...dotnetArgs],
      {
        env: {
          ...process.env,
          ...NO_TELEMETRY,
          CONTRACT_API_URL: API_URL,
          CONTRACT_WEB_ORIGIN: WEB_ORIGIN,
        },
      },
    );
    const testsCode = await exitCodeOf(tests);
    const openApiCode = dotnet ? await compareOpenApi() : 0;
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
