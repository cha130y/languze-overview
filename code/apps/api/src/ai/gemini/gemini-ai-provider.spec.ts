import type {
  Content,
  FunctionDeclaration,
  GenerateContentConfig,
  GenerateContentParameters,
  GenerateContentResponse,
  Part,
} from '@google/genai';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { checkedItems } from '../../vocabulary/extraction-rules.js';
import {
  AiProviderError,
  type TutorEvent,
  type TutorTool,
  type TutorTurn,
} from '../ai-provider.js';
import { GeminiAiProvider, type GeminiModels } from './gemini-ai-provider.js';

const photo = { data: Buffer.from('a photo'), contentType: 'image/jpeg' };

const settings = {
  safetyModel: 'safety-model',
  extractionModel: 'extraction-model',
  tutorModel: 'tutor-model',
  timeoutMs: 30_000,
  tutorTimeoutMs: 30_000,
  maxToolRounds: 5,
};

/** A stand-in for the SDK: whatever the test says Gemini answered. */
function gemini(answer: Partial<GenerateContentResponse> | Error) {
  const generateContent = vi.fn(() =>
    answer instanceof Error
      ? Promise.reject(answer)
      : Promise.resolve(answer as GenerateContentResponse),
  );
  const models = { generateContent } as unknown as GeminiModels;
  return {
    provider: new GeminiAiProvider(models, settings),
    generateContent,
  };
}

const answering = (
  body: unknown,
  extra: Partial<GenerateContentResponse> = {},
) =>
  ({
    text: JSON.stringify(body),
    usageMetadata: { promptTokenCount: 900, candidatesTokenCount: 120 },
    ...extra,
  }) as Partial<GenerateContentResponse>;

const requestOf = (generateContent: ReturnType<typeof vi.fn>) =>
  generateContent.mock.calls[0][0] as GenerateContentParameters;

const item = (overrides: Record<string, unknown> = {}) => ({
  english: 'sofa',
  thaiMeaning: 'โซฟา',
  exampleSentence: 'We sit on the sofa.',
  cefrLevel: 'A1',
  acceptedVariants: ['couch'],
  // [ymin, xmin, ymax, xmax], 0 to 1000 (ADR-0004).
  box: [100, 200, 500, 600],
  ...overrides,
});

beforeEach(() => vi.clearAllMocks());

describe('the safety check (FR-092)', () => {
  it('allows a photo Gemini allows', async () => {
    const { provider } = gemini(answering({ allowed: true }));

    const { value, usage } = await provider.checkPhoto(photo);

    expect(value).toEqual({ allowed: true });
    expect(usage).toEqual({ inputTokens: 900, outputTokens: 120 });
  });

  it('blocks with the category Gemini chose', async () => {
    const { provider } = gemini(
      answering({ allowed: false, category: 'VIOLENCE' }),
    );

    expect((await provider.checkPhoto(photo)).value).toEqual({
      allowed: false,
      category: 'VIOLENCE',
    });
  });

  it('falls back to OTHER for a category LanguZe does not know', async () => {
    const { provider } = gemini(
      answering({ allowed: false, category: 'SOMETHING_ELSE' }),
    );

    expect((await provider.checkPhoto(photo)).value).toEqual({
      allowed: false,
      category: 'OTHER',
    });
  });

  /* ADR-0004: a photo Gemini's own filters refuse is one LanguZe should not keep. */
  it('treats Gemini refusing to look as a block', async () => {
    const { provider } = gemini(
      answering({}, {
        promptFeedback: {
          blockReason: 'SAFETY',
          safetyRatings: [
            { category: 'HARM_CATEGORY_SEXUALLY_EXPLICIT', blocked: true },
          ],
        },
      } as Partial<GenerateContentResponse>),
    );

    expect((await provider.checkPhoto(photo)).value).toEqual({
      allowed: false,
      category: 'SEXUAL_CONTENT',
    });
  });

  it('blocks as OTHER when the refusal names no category', async () => {
    const { provider } = gemini(
      answering({}, {
        promptFeedback: { blockReason: 'OTHER' },
      } as Partial<GenerateContentResponse>),
    );

    expect((await provider.checkPhoto(photo)).value).toEqual({
      allowed: false,
      category: 'OTHER',
    });
  });

  it('asks the safety model, with the photo and fixed instructions only', async () => {
    const { provider, generateContent } = gemini(answering({ allowed: true }));

    await provider.checkPhoto(photo);

    const request = requestOf(generateContent);
    expect(request.model).toBe('safety-model');
    const sent = JSON.stringify(request);
    expect(sent).toContain(photo.data.toString('base64'));
    // Nothing about the learner may travel with a photo (AIR-006).
    expect(sent).not.toMatch(/email|learner|@/i);
  });

  it('refuses an answer that gives no verdict', async () => {
    const { provider } = gemini(answering({ category: 'VIOLENCE' }));

    await expect(provider.checkPhoto(photo)).rejects.toMatchObject({
      code: 'INVALID_OUTPUT',
    });
  });
});

describe('extraction (AIR-003, ADR-0004)', () => {
  it('turns Gemini’s boxes into LanguZe’s', async () => {
    const { provider } = gemini(answering({ items: [item()] }));

    const { value } = await provider.extractVocabulary(photo);

    // [100, 200, 500, 600] of 1000 is x 0.2, y 0.1, width 0.4, height 0.4.
    expect(value[0].box).toEqual({
      x: 0.2,
      y: 0.1,
      width: 0.4,
      height: 0.4,
    });
  });

  it('produces items the vocabulary rules accept', async () => {
    const { provider } = gemini(answering({ items: [item()] }));

    const { value } = await provider.extractVocabulary(photo);

    expect(checkedItems(value)).toHaveLength(1);
  });

  it('asks the extraction model and reports what it cost', async () => {
    const { provider, generateContent } = gemini(
      answering({ items: [item()] }),
    );

    const { usage } = await provider.extractVocabulary(photo);

    expect(requestOf(generateContent).model).toBe('extraction-model');
    expect(usage).toEqual({ inputTokens: 900, outputTokens: 120 });
  });

  /* A misshapen item is not judged here: the item rules decide, in one place. */
  it('passes a broken item on rather than guessing', async () => {
    const { provider } = gemini(
      answering({ items: [item({ box: [1, 2], english: undefined })] }),
    );

    const { value } = await provider.extractVocabulary(photo);

    expect(value[0].english).toBe('');
    expect(checkedItems(value)).toEqual([]);
  });

  it('refuses an answer with no list of items', async () => {
    const { provider } = gemini(answering({ words: [] }));

    await expect(provider.extractVocabulary(photo)).rejects.toMatchObject({
      code: 'INVALID_OUTPUT',
    });
  });

  /* The photo already passed the safety check, so a refusal here is the provider. */
  it('treats a refusal as a provider error, not a block', async () => {
    const { provider } = gemini(
      answering({}, {
        candidates: [{ finishReason: 'SAFETY' }],
      } as Partial<GenerateContentResponse>),
    );

    await expect(provider.extractVocabulary(photo)).rejects.toMatchObject({
      code: 'PROVIDER_ERROR',
    });
  });
});

describe('when the call itself fails', () => {
  it('reports a timeout as a timeout', async () => {
    const aborted = new Error('The operation was aborted');
    aborted.name = 'TimeoutError';
    const { provider } = gemini(aborted);

    await expect(provider.checkPhoto(photo)).rejects.toMatchObject({
      code: 'TIMED_OUT',
    });
  });

  it('reports anything else as a provider error', async () => {
    const { provider } = gemini(new Error('503 Service Unavailable'));

    await expect(provider.extractVocabulary(photo)).rejects.toBeInstanceOf(
      AiProviderError,
    );
    await expect(provider.extractVocabulary(photo)).rejects.toMatchObject({
      code: 'PROVIDER_ERROR',
    });
  });

  it('refuses an empty answer', async () => {
    const { provider } = gemini({ text: undefined });

    await expect(provider.checkPhoto(photo)).rejects.toMatchObject({
      code: 'INVALID_OUTPUT',
    });
  });

  it('refuses an answer that is not JSON', async () => {
    const { provider } = gemini({ text: 'I am afraid I cannot do that.' });

    await expect(provider.extractVocabulary(photo)).rejects.toMatchObject({
      code: 'INVALID_OUTPUT',
    });
  });
});

describe('which model answers which question', () => {
  it('uses the model configured for each job', () => {
    const { provider } = gemini(answering({}));

    expect(provider.name).toBe('gemini');
    expect(provider.modelFor('SAFETY_CHECK')).toBe('safety-model');
    expect(provider.modelFor('EXTRACTION')).toBe('extraction-model');
    expect(provider.modelFor('TUTOR')).toBe('tutor-model');
  });
});

/* The tutor (FR-070, FR-072, FR-073, NFR-003). */
describe('the tutor', () => {
  /** One streamed round: the chunks Gemini sends back for a single call. */
  type Round = Partial<GenerateContentResponse>[];

  /** A chunk as the SDK delivers it: Gemini's parts, under its one candidate. */
  const partsChunk = (...parts: Part[]) =>
    ({
      candidates: [{ content: { role: 'model', parts } }],
    }) as Partial<GenerateContentResponse>;

  const said = (text: string, extra: Partial<GenerateContentResponse> = {}) =>
    ({ ...partsChunk({ text }), ...extra }) as Partial<GenerateContentResponse>;

  const asked = (
    name: string,
    args: Record<string, unknown> = {},
    thoughtSignature?: string,
  ) =>
    partsChunk({
      functionCall: { name, args },
      ...(thoughtSignature ? { thoughtSignature } : {}),
    });

  const spent = (inputTokens: number, outputTokens: number) =>
    ({
      usageMetadata: {
        promptTokenCount: inputTokens,
        candidatesTokenCount: outputTokens,
      },
    }) as Partial<GenerateContentResponse>;

  async function* chunksOf(round: Round) {
    for (const chunk of round) {
      yield await Promise.resolve(chunk as GenerateContentResponse);
    }
  }

  /** A stand-in for the SDK that streams the rounds the test wrote out. */
  function streaming(...rounds: (Round | Error)[]) {
    let next = 0;
    const generateContentStream = vi.fn(() => {
      const round = rounds[Math.min(next, rounds.length - 1)];
      next += 1;
      if (round instanceof Error) return Promise.reject(round);
      return Promise.resolve(chunksOf(round));
    });
    const models = {
      generateContentStream,
    } as unknown as GeminiModels;
    return {
      provider: new GeminiAiProvider(models, settings),
      generateContentStream,
    };
  }

  const weakWords = vi.fn(() =>
    Promise.resolve({ words: [{ english: 'sofa' }] }),
  );

  const tools: TutorTool[] = [
    {
      name: 'weak_words',
      description: 'The learner’s weakest words.',
      parameters: [
        { name: 'count', type: 'integer', description: 'How many.' },
      ],
      run: weakWords,
    },
  ];

  const turn = (overrides: Partial<TutorTurn> = {}): TutorTurn => ({
    history: [],
    message: 'ทำไมฉันจำคำนี้ไม่ได้',
    tools,
    ...overrides,
  });

  async function collect(
    provider: GeminiAiProvider,
    only: TutorTurn = turn(),
  ): Promise<TutorEvent[]> {
    const events: TutorEvent[] = [];
    for await (const event of provider.tutorReply(only)) events.push(event);
    return events;
  }

  const requestFor = (
    stream: ReturnType<typeof vi.fn>,
    round: number,
  ): GenerateContentParameters =>
    stream.mock.calls[round][0] as GenerateContentParameters;

  const configOf = (
    stream: ReturnType<typeof vi.fn>,
    round: number,
  ): GenerateContentConfig => requestFor(stream, round).config ?? {};

  const contentsOf = (
    stream: ReturnType<typeof vi.fn>,
    round: number,
  ): Content[] => {
    const { contents } = requestFor(stream, round);
    if (!Array.isArray(contents)) throw new Error('expected a conversation');
    return contents as Content[];
  };

  const declarationsOf = (
    stream: ReturnType<typeof vi.fn>,
    round: number,
  ): FunctionDeclaration[] => {
    const [tool] = configOf(stream, round).tools ?? [];
    if (!tool || !('functionDeclarations' in tool)) {
      throw new Error('expected declared tools');
    }
    return tool.functionDeclarations ?? [];
  };

  const textOf = (events: TutorEvent[]) =>
    events
      .filter((event) => event.type === 'delta')
      .map((event) => event.text)
      .join('');

  beforeEach(() => {
    weakWords.mockClear();
    weakWords.mockResolvedValue({ words: [{ english: 'sofa' }] });
  });

  /* NFR-003: the learner watches the reply being written. */
  it('streams the reply as it arrives', async () => {
    const { provider } = streaming([
      said('คำนี้'),
      said('ใช้บอก'),
      said('สิ่งของ', spent(900, 120)),
    ]);

    const events = await collect(provider);

    expect(events.filter((event) => event.type === 'delta')).toHaveLength(3);
    expect(textOf(events)).toBe('คำนี้ใช้บอกสิ่งของ');
    expect(events.at(-1)).toEqual({
      type: 'done',
      usage: { inputTokens: 900, outputTokens: 120 },
    });
  });

  it('sends the conversation oldest first, with the new message last', async () => {
    const { provider, generateContentStream } = streaming([said('ค่ะ')]);

    await collect(
      provider,
      turn({
        history: [
          { role: 'LEARNER', content: 'สวัสดี' },
          { role: 'TUTOR', content: 'สวัสดีค่ะ' },
        ],
      }),
    );

    expect(contentsOf(generateContentStream, 0)).toEqual([
      { role: 'user', parts: [{ text: 'สวัสดี' }] },
      { role: 'model', parts: [{ text: 'สวัสดีค่ะ' }] },
      { role: 'user', parts: [{ text: 'ทำไมฉันจำคำนี้ไม่ได้' }] },
    ]);
  });

  describe('looking the learner up (FR-072)', () => {
    /*
     * Gemini signs the function calls it makes, and refuses the next request
     * unless its turn comes back as it was sent (HTTP 400). Every tool reply
     * failed against the real Gemini until the turn was sent back whole.
     */
    it("sends Gemini's turn back as it came, thought signature and all", async () => {
      const { provider, generateContentStream } = streaming(
        [asked('weak_words', { count: 3 }, 'signature-1'), spent(400, 10)],
        [said('คำที่ยากคือ sofa'), spent(600, 40)],
      );

      await collect(provider);

      expect(contentsOf(generateContentStream, 1)[1]).toEqual({
        role: 'model',
        parts: [
          {
            functionCall: { name: 'weak_words', args: { count: 3 } },
            thoughtSignature: 'signature-1',
          },
        ],
      });
    });

    it('never shows the learner a part marked as a thought', async () => {
      const { provider } = streaming([
        partsChunk({ text: 'Let me think about sofa.', thought: true }),
        said('คำนี้คือ sofa'),
        spent(400, 10),
      ]);

      expect(textOf(await collect(provider))).toBe('คำนี้คือ sofa');
    });

    it('runs the tool, feeds the result back, and answers', async () => {
      const { provider, generateContentStream } = streaming(
        [asked('weak_words', { count: 3 }), spent(400, 10)],
        [said('คำที่ยากคือ sofa', {}), spent(600, 40)],
      );

      const events = await collect(provider);

      expect(weakWords).toHaveBeenCalledWith({ count: 3 });
      expect(textOf(events)).toBe('คำที่ยากคือ sofa');

      // The second call carries Gemini's request and LanguZe's answer to it.
      const contents = contentsOf(generateContentStream, 1);
      expect(contents).toHaveLength(3);
      expect(contents[1]).toEqual({
        role: 'model',
        parts: [{ functionCall: { name: 'weak_words', args: { count: 3 } } }],
      });
      expect(contents[2]).toEqual({
        role: 'user',
        parts: [
          {
            functionResponse: {
              name: 'weak_words',
              response: { words: [{ english: 'sofa' }] },
            },
          },
        ],
      });
    });

    /* Every round is billed, so the learner's message costs all of them. */
    it('adds up what every round cost', async () => {
      const { provider } = streaming(
        [asked('weak_words'), spent(400, 10)],
        [said('พร้อมแล้วค่ะ'), spent(600, 40)],
      );

      const events = await collect(provider);

      expect(events.at(-1)).toEqual({
        type: 'done',
        usage: { inputTokens: 1000, outputTokens: 50 },
      });
    });

    it('describes each tool to Gemini without a learner anywhere in it', async () => {
      const { provider, generateContentStream } = streaming([said('ค่ะ')]);

      await collect(provider);

      const declared = declarationsOf(generateContentStream, 0);
      expect(declared.map((one) => one.name)).toEqual(['weak_words']);
      /*
       * FR-072 again, at the protocol boundary. A description says whose words
       * these are, and must; it is the parameters Gemini fills in that may never
       * name a learner, because those are the only part the model writes.
       */
      const fillable = declared.flatMap((one) =>
        Object.keys(one.parameters?.properties ?? {}),
      );
      expect(fillable).toEqual(['count']);
      expect(fillable.join(' ')).not.toMatch(/learner|user|account/i);
    });

    /* A model inventing a tool is not a reason to fail the learner's message. */
    it('tells Gemini when it asks for a tool that does not exist', async () => {
      const { provider, generateContentStream } = streaming(
        [asked('other_learners')],
        [said('ขออภัยค่ะ')],
      );

      const events = await collect(provider);

      const answered = contentsOf(generateContentStream, 1)[2];
      const response = answered.parts?.[0].functionResponse;
      expect(response?.name).toBe('other_learners');
      expect(String(response?.response?.error)).toContain('no tool');
      expect(textOf(events)).toBe('ขออภัยค่ะ');
    });

    /*
     * ADR-0004 gives the whole reply 30 seconds, not 30 per round: otherwise five
     * slow lookups could keep a learner waiting five times the limit. The same
     * signal reaching every round is what makes the deadline mean the whole turn.
     */
    it('gives the whole turn one deadline, not one for each round', async () => {
      const { provider, generateContentStream } = streaming(
        [asked('weak_words')],
        [said('ค่ะ')],
      );

      await collect(provider);

      const first = configOf(generateContentStream, 0).abortSignal;
      const second = configOf(generateContentStream, 1).abortSignal;
      expect(first).toBeInstanceOf(AbortSignal);
      expect(second).toBe(first);
    });

    /* ADR-0004 bounds one message to 5 tool rounds; past that it costs without end. */
    it('stops a model that only ever looks things up', async () => {
      const { provider, generateContentStream } = streaming([
        asked('weak_words'),
      ]);

      await expect(collect(provider)).rejects.toMatchObject({
        code: 'PROVIDER_ERROR',
      });
      expect(generateContentStream).toHaveBeenCalledTimes(
        settings.maxToolRounds + 1,
      );
      expect(weakWords).toHaveBeenCalledTimes(settings.maxToolRounds);
    });
  });

  /* AIR-006: the turn holds nothing but the conversation, so nothing else is sent. */
  it('sends nothing that identifies the learner', async () => {
    const { provider, generateContentStream } = streaming([said('ค่ะ')]);

    await collect(provider);

    expect(requestFor(generateContentStream, 0).model).toBe('tutor-model');
    expect(Object.keys(configOf(generateContentStream, 0)).sort()).toEqual([
      'abortSignal',
      'systemInstruction',
      'tools',
    ]);
  });

  describe('when it fails (FR-071, US-070 criterion 5)', () => {
    it('reports a timeout as a timeout', async () => {
      const aborted = new Error('The operation was aborted');
      aborted.name = 'TimeoutError';
      const { provider } = streaming(aborted);

      await expect(collect(provider)).rejects.toMatchObject({
        code: 'TIMED_OUT',
      });
    });

    it('reports anything else as a provider error', async () => {
      const { provider } = streaming(new Error('503 Service Unavailable'));

      await expect(collect(provider)).rejects.toBeInstanceOf(AiProviderError);
    });

    /* Gemini's own filters refusing leave no reply; an empty one must not be saved. */
    it('refuses an answer with nothing in it', async () => {
      const { provider } = streaming([said('')]);

      await expect(collect(provider)).rejects.toMatchObject({
        code: 'INVALID_OUTPUT',
      });
    });

    it('fails the turn when a tool itself fails', async () => {
      weakWords.mockRejectedValue(new Error('the database is down'));
      const { provider } = streaming([asked('weak_words')], [said('ค่ะ')]);

      await expect(collect(provider)).rejects.toThrow('the database is down');
    });
  });
});
