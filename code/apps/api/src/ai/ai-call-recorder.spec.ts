import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PrismaService } from '../prisma/prisma.service.js';
import { AiCallRecorder } from './ai-call-recorder.js';
import { AiProviderError } from './ai-provider.js';

const details = {
  purpose: 'EXTRACTION',
  provider: 'fake',
  model: 'fake-1',
} as const;

/** A database that remembers the row instead of writing it. */
function recorderWithSpy(options: { failsToWrite?: boolean } = {}) {
  const create = vi.fn(
    options.failsToWrite
      ? () => Promise.reject(new Error('the database is down'))
      : () => Promise.resolve({}),
  );
  const prisma = { aiCall: { create } } as unknown as PrismaService;
  return { recorder: new AiCallRecorder(prisma), create };
}

const rowOf = (create: ReturnType<typeof vi.fn>) =>
  (create.mock.calls[0]?.[0] as { data: Record<string, unknown> }).data;

describe('AiCallRecorder (AIR-007)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('records a call that worked, with what it cost', async () => {
    const { recorder, create } = recorderWithSpy();

    const answer = await recorder.record(details, () =>
      Promise.resolve({
        value: ['sofa'],
        usage: { inputTokens: 1200, outputTokens: 80 },
      }),
    );

    expect(answer).toEqual(['sofa']);
    expect(rowOf(create)).toMatchObject({
      purpose: 'EXTRACTION',
      provider: 'fake',
      model: 'fake-1',
      outcome: 'SUCCEEDED',
      inputTokens: 1200,
      outputTokens: 80,
      errorCode: null,
    });
    expect(rowOf(create).latencyMs).toBeTypeOf('number');
  });

  it('keeps no learner data in the row', async () => {
    const { recorder, create } = recorderWithSpy();

    await recorder.record(details, () => Promise.resolve({ value: 'answer' }));

    // Everything the row may hold; anything else would be a leak (AIR-007).
    expect(Object.keys(rowOf(create)).sort()).toEqual([
      'errorCode',
      'inputTokens',
      'latencyMs',
      'model',
      'outcome',
      'outputTokens',
      'provider',
      'purpose',
      'requestId',
      'startedAt',
    ]);
  });

  it.each([
    ['TIMED_OUT', 'TIMED_OUT'],
    ['INVALID_OUTPUT', 'INVALID_OUTPUT'],
    ['PROVIDER_ERROR', 'FAILED'],
  ] as const)('records a %s call as %s', async (code, outcome) => {
    const { recorder, create } = recorderWithSpy();

    await expect(
      recorder.record(details, () =>
        Promise.reject(new AiProviderError(code, 'no answer')),
      ),
    ).rejects.toBeInstanceOf(AiProviderError);

    expect(rowOf(create)).toMatchObject({ outcome, errorCode: code });
  });

  it('records an error from somewhere else as a failure', async () => {
    const { recorder, create } = recorderWithSpy();

    await expect(
      recorder.record(details, () => Promise.reject(new TypeError('broken'))),
    ).rejects.toBeInstanceOf(TypeError);

    expect(rowOf(create)).toMatchObject({
      outcome: 'FAILED',
      errorCode: 'TypeError',
    });
  });

  /* A cost record is worth less than the answer it describes. */
  it('still answers when the record cannot be written', async () => {
    const { recorder } = recorderWithSpy({ failsToWrite: true });

    await expect(
      recorder.record(details, () => Promise.resolve({ value: 'answer' })),
    ).resolves.toBe('answer');
  });

  it('still throws the provider error when the record cannot be written', async () => {
    const { recorder } = recorderWithSpy({ failsToWrite: true });

    await expect(
      recorder.record(details, () =>
        Promise.reject(new AiProviderError('TIMED_OUT', 'no answer')),
      ),
    ).rejects.toBeInstanceOf(AiProviderError);
  });
});
