import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AiCallRecorder } from '../ai/ai-call-recorder.js';
import {
  AiProviderError,
  type AiProvider,
  type TutorEvent,
} from '../ai/ai-provider.js';
import type { AuthService } from '../auth/auth.service.js';
import type { SuspensionService } from '../moderation/suspension.service.js';
import { AppError } from '../platform/errors/app-error.js';
import {
  TutorReplyService,
  replyErrorCode,
  type TutorTurnInProgress,
} from './tutor-reply.service.js';
import type { TutorToolsService } from './tutor-tools.service.js';
import type { TutorService } from './tutor.service.js';

const DAY = new Date('2026-09-26T00:00:00Z');

const turn: TutorTurnInProgress = {
  learnerId: 'learner-1',
  content: 'sofa แปลว่าอะไร',
  day: DAY,
  messagesLeft: 28,
};

/** A provider that says a few things and then gives up. */
function buildFailing(said: TutorEvent, failure: Error) {
  return build({ said, failure });
}

/** Only what this service calls; the rest of each collaborator is not its business. */
function build(
  ...events: (TutorEvent | { said: TutorEvent; failure: Error })[]
) {
  const release = vi.fn(() => Promise.resolve());
  const remember = vi.fn(() =>
    Promise.resolve({ messageId: 'message-1', replyId: 'reply-1' }),
  );

  const tutor = {
    remembered: () => Promise.resolve([]),
    remember,
    release,
  } as unknown as TutorService;

  const tools = { forLearner: () => [] } as unknown as TutorToolsService;

  const ai = {
    name: 'fake',
    modelFor: () => 'a-model',
    // eslint-disable-next-line @typescript-eslint/require-await
    tutorReply: async function* () {
      for (const event of events) {
        if ('failure' in event) {
          yield event.said;
          throw event.failure;
        }
        yield event;
      }
    },
  } as unknown as AiProvider;

  // The recorder writes a row and passes the events straight through (AIR-007).
  const recorder = {
    stream: (_details: unknown, passed: AsyncIterable<TutorEvent>) => passed,
  } as unknown as AiCallRecorder;

  const auth = {
    isVerifiedForAi: () => Promise.resolve(true),
  } as unknown as AuthService;

  // Refusing is this service's own concern and has its own tests; here it allows.
  const suspensions = {
    requireAccess: () => Promise.resolve(),
  } as unknown as SuspensionService;

  return {
    service: new TutorReplyService(
      tutor,
      tools,
      ai,
      recorder,
      auth,
      suspensions,
    ),
    release,
    remember,
  };
}

const collect = async (service: TutorReplyService) => {
  const seen = [];
  for await (const event of service.stream(turn)) seen.push(event);
  return seen;
};

beforeEach(() => vi.clearAllMocks());

/* FR-071, S6: a message only counts when the learner got an answer for it. */
describe('a reply that never arrives', () => {
  /*
   * A provider can end a stream without saying anything and without failing. The
   * conversation must not gain an empty reply, and the learner must not pay for
   * one, so this is a failure like any other.
   */
  it('refuses an answer with no text in it', async () => {
    const { service, release, remember } = build({ type: 'done' });

    await expect(collect(service)).rejects.toBeInstanceOf(AiProviderError);

    expect(remember).not.toHaveBeenCalled();
    expect(release).toHaveBeenCalledWith('learner-1', DAY);
  });

  it('treats whitespace as nothing at all', async () => {
    const { service, remember } = build(
      { type: 'delta', text: '   ' },
      { type: 'done' },
    );

    await expect(collect(service)).rejects.toBeInstanceOf(AiProviderError);
    expect(remember).not.toHaveBeenCalled();
  });

  /* Half an answer is not an answer: the learner should not pay for one. */
  it('saves nothing when the provider fails part way through', async () => {
    const { service, release, remember } = buildFailing(
      { type: 'delta', text: 'เริ่ม' },
      new AiProviderError('PROVIDER_ERROR', 'Gemini could not answer'),
    );

    await expect(collect(service)).rejects.toBeInstanceOf(AiProviderError);

    expect(remember).not.toHaveBeenCalled();
    expect(release).toHaveBeenCalledWith('learner-1', DAY);
  });
});

describe('a reply that arrives', () => {
  it('saves it once, whole, and reports what is left', async () => {
    const { service, release, remember } = build(
      { type: 'delta', text: 'คำนี้' },
      { type: 'delta', text: 'ใช้บอกสิ่งของ' },
      { type: 'done', usage: { inputTokens: 400, outputTokens: 60 } },
    );

    const seen = await collect(service);

    expect(remember).toHaveBeenCalledTimes(1);
    expect(remember).toHaveBeenCalledWith(
      'learner-1',
      turn.content,
      'คำนี้ใช้บอกสิ่งของ',
    );
    expect(release).not.toHaveBeenCalled();
    expect(seen.at(-1)).toEqual({
      event: 'done',
      data: { messageId: 'message-1', replyId: 'reply-1', messagesLeft: 28 },
    });
  });

  /* Saving is what may still fail last; the message goes back if it does. */
  it('gives the message back when it cannot be saved', async () => {
    const { service, release, remember } = build(
      { type: 'delta', text: 'คำนี้' },
      { type: 'done' },
    );
    remember.mockRejectedValue(new Error('the database is down'));

    await expect(collect(service)).rejects.toThrow('the database is down');
    expect(release).toHaveBeenCalledWith('learner-1', DAY);
  });
});

/** What the browser is told, which decides the Thai text a learner sees. */
describe('the code a failure is reported by', () => {
  it('keeps a code LanguZe chose on purpose', () => {
    const refused = new AppError('DAILY_TUTOR_LIMIT', 429, 'No messages left.');

    expect(replyErrorCode(refused)).toBe('DAILY_TUTOR_LIMIT');
  });

  it('reports any provider failure as the provider being unavailable', () => {
    expect(replyErrorCode(new AiProviderError('TIMED_OUT', 'too slow'))).toBe(
      'AI_PROVIDER_UNAVAILABLE',
    );
  });

  it('reports anything else as an internal error', () => {
    expect(replyErrorCode(new Error('a bug'))).toBe('INTERNAL_ERROR');
  });
});
