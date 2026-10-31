import { beforeEach, describe, expect, it, vi } from 'vitest';
import { checkedItems } from '../vocabulary/extraction-rules.js';
import {
  AiProviderError,
  type TutorEvent,
  type TutorTool,
} from './ai-provider.js';
import { FakeAiProvider } from './fake-ai-provider.js';

const photo = { data: Buffer.from('photo'), contentType: 'image/jpeg' };

describe('FakeAiProvider (B4)', () => {
  let provider: FakeAiProvider;

  beforeEach(() => {
    provider = new FakeAiProvider();
  });

  it('allows a photo and describes it', async () => {
    expect((await provider.checkPhoto(photo)).value).toEqual({ allowed: true });

    const { value: items } = await provider.extractVocabulary(photo);

    expect(items.length).toBeGreaterThanOrEqual(3);
    expect(items[0].english).toBe('sofa');
  });

  /* The fixed answer is only useful if it survives the rules the real one must pass. */
  it('returns items that all pass the extraction rules', async () => {
    const { value: items } = await provider.extractVocabulary(photo);

    expect(checkedItems(items)).toHaveLength(items.length);
  });

  /* What each call cost travels with the answer, so it can be recorded (AIR-007). */
  it('reports what a call cost', async () => {
    const { usage } = await provider.extractVocabulary(photo);

    expect(usage?.inputTokens).toBeGreaterThan(0);
    expect(usage?.outputTokens).toBeGreaterThan(0);
  });

  it('names itself and its model for the call record', () => {
    expect(provider.name).toBe('fake');
    expect(provider.modelFor('EXTRACTION')).toBe('fake-extraction');
  });

  it('blocks a photo with a category when asked', async () => {
    provider.behaviour = 'BLOCKED';

    expect((await provider.checkPhoto(photo)).value).toEqual({
      allowed: false,
      category: 'VIOLENCE',
    });
  });

  it.each([
    ['PROVIDER_ERROR', 'PROVIDER_ERROR'],
    ['TIMED_OUT', 'TIMED_OUT'],
  ] as const)('fails as %s when asked', async (behaviour, code) => {
    provider.behaviour = behaviour;

    await expect(provider.checkPhoto(photo)).rejects.toMatchObject({ code });
    await expect(provider.extractVocabulary(photo)).rejects.toBeInstanceOf(
      AiProviderError,
    );
  });

  it('returns too few words when asked (FR-024)', async () => {
    provider.behaviour = 'TOO_FEW_WORDS';

    expect((await provider.extractVocabulary(photo)).value).toHaveLength(2);
  });

  /* What a bad response looks like: every item is refused, so nothing can be saved. */
  it('returns output that fails every rule when asked', async () => {
    provider.behaviour = 'INVALID_OUTPUT';

    const { value: items } = await provider.extractVocabulary(photo);

    expect(items.length).toBeGreaterThan(0);
    expect(checkedItems(items)).toEqual([]);
  });

  describe('behaveOnce', () => {
    it('affects the next call only', async () => {
      provider.behaveOnce('BLOCKED');

      expect((await provider.checkPhoto(photo)).value).toEqual({
        allowed: false,
        category: 'VIOLENCE',
      });
      // The standing behaviour is back, which is how a test fails one call of two.
      expect((await provider.checkPhoto(photo)).value).toEqual({
        allowed: true,
      });
    });

    it('lets the check pass and the extraction fail', async () => {
      expect((await provider.checkPhoto(photo)).value).toEqual({
        allowed: true,
      });
      provider.behaveOnce('PROVIDER_ERROR');

      await expect(provider.extractVocabulary(photo)).rejects.toBeInstanceOf(
        AiProviderError,
      );
    });
  });

  it('remembers what it was asked, in order', async () => {
    await provider.checkPhoto(photo);
    await provider.extractVocabulary(photo);

    expect(provider.calls.map((call) => call.purpose)).toEqual([
      'SAFETY_CHECK',
      'EXTRACTION',
    ]);
  });
});

/* Development-only: the real analysis takes about 20 seconds, the fake none at all. */
describe('the delay a developer can ask for', () => {
  it('answers at once by default', async () => {
    const provider = new FakeAiProvider();

    await expect(provider.checkPhoto(photo)).resolves.toMatchObject({
      value: { allowed: true },
    });
  });

  it('waits that long before answering, and before failing', async () => {
    vi.useFakeTimers();
    try {
      const provider = new FakeAiProvider();
      provider.delayMs = 5_000;

      const answer = provider.extractVocabulary(photo);
      let settled = false;
      void answer.then(() => (settled = true));
      await vi.advanceTimersByTimeAsync(4_000);
      expect(settled).toBe(false);

      await vi.advanceTimersByTimeAsync(1_000);
      expect(settled).toBe(true);

      provider.behaviour = 'PROVIDER_ERROR';
      const failing = provider.checkPhoto(photo);
      const caught = expect(failing).rejects.toBeInstanceOf(AiProviderError);
      await vi.advanceTimersByTimeAsync(5_000);
      await caught;
    } finally {
      vi.useRealTimers();
    }
  });
});

/** The tutor half of the fake (FR-070, NFR-003). */
describe('the fake tutor', () => {
  const MESSAGE = 'ทำไมฉันจำคำนี้ไม่ได้';

  const weakWords = (result: unknown): TutorTool => ({
    name: 'weak_words',
    description: 'The learner’s weakest words.',
    parameters: [],
    run: () => Promise.resolve(result),
  });

  async function collect(
    provider: FakeAiProvider,
    tools: TutorTool[] = [],
  ): Promise<TutorEvent[]> {
    const events: TutorEvent[] = [];
    for await (const event of provider.tutorReply({
      history: [],
      message: MESSAGE,
      tools,
    })) {
      events.push(event);
    }
    return events;
  }

  it('answers in pieces, and says what it cost at the end', async () => {
    const provider = new FakeAiProvider();

    const events = await collect(provider);

    expect(
      events.filter((event) => event.type === 'delta').length,
    ).toBeGreaterThan(1);
    expect(events.at(-1)).toEqual({
      type: 'done',
      usage: { inputTokens: 400, outputTokens: 60 },
    });
    expect(provider.calls).toEqual([
      { purpose: 'TUTOR', bytes: MESSAGE.length },
    ]);
  });

  /* The tool path is the part worth exercising without a key (FR-072). */
  it('looks the learner up through the tool it was given', async () => {
    const provider = new FakeAiProvider();
    const words = { words: [{ english: 'sofa' }] };

    await collect(provider, [weakWords(words)]);

    expect(provider.lastToolResult).toEqual(words);
  });

  it('manages without any tools at all', async () => {
    const provider = new FakeAiProvider();

    const events = await collect(provider);

    expect(provider.lastToolResult).toBeNull();
    expect(events.some((event) => event.type === 'delta')).toBe(true);
  });

  /* A provider failure must reach the caller, which releases the message (S6). */
  it('fails the way a provider fails', async () => {
    const provider = new FakeAiProvider();
    provider.behaviour = 'PROVIDER_ERROR';

    await expect(collect(provider)).rejects.toBeInstanceOf(AiProviderError);
  });
});
