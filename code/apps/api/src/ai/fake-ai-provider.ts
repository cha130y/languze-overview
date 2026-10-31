import type { BlockCategory } from '../generated/prisma/enums.js';
import { Injectable } from '@nestjs/common';
import type { ExtractedItem } from '../vocabulary/extraction-rules.js';
import type { AiPurpose } from '../generated/prisma/enums.js';
import {
  AiProvider,
  AiProviderError,
  type AiAnswer,
  type PhotoForAi,
  type SafetyVerdict,
  type TutorEvent,
  type TutorExchange,
  type TutorTurn,
} from './ai-provider.js';

/**
 * What the fake provider does next, which a test sets and a developer can choose
 * with `AI_FAKE_BEHAVIOUR` to see each path in the browser.
 */
export type FakeBehaviour =
  | 'ALLOWED'
  | 'BLOCKED'
  | 'PROVIDER_ERROR'
  | 'TIMED_OUT'
  | 'TOO_FEW_WORDS'
  | 'INVALID_OUTPUT';

/**
 * A photo of a living room, as a provider would describe it: ordered by confidence,
 * with boxes that do not overlap much and sentences an A1–B1 learner can read.
 */
const LIVING_ROOM: ExtractedItem[] = [
  {
    english: 'sofa',
    thaiMeaning: 'โซฟา',
    exampleSentence: 'We sit on the sofa every evening.',
    cefrLevel: 'A1',
    acceptedVariants: ['couch'],
    box: { x: 0.05, y: 0.45, width: 0.4, height: 0.35 },
  },
  {
    english: 'curtain',
    thaiMeaning: 'ผ้าม่าน',
    exampleSentence: 'Please open the curtain in the morning.',
    cefrLevel: 'A2',
    acceptedVariants: ['curtains', 'drape'],
    box: { x: 0.55, y: 0.05, width: 0.3, height: 0.6 },
  },
  {
    english: 'coffee table',
    thaiMeaning: 'โต๊ะกลาง',
    exampleSentence: 'My book is on the coffee table.',
    cefrLevel: 'A2',
    acceptedVariants: ['table'],
    box: { x: 0.2, y: 0.7, width: 0.25, height: 0.18 },
  },
  {
    english: 'rug',
    thaiMeaning: 'พรม',
    exampleSentence: 'The rug under the table is soft.',
    cefrLevel: 'B1',
    acceptedVariants: ['carpet'],
    box: { x: 0.12, y: 0.78, width: 0.5, height: 0.18 },
  },
  {
    english: 'plant',
    thaiMeaning: 'ต้นไม้ในกระถาง',
    exampleSentence: 'I water the plant on Sunday.',
    cefrLevel: 'A1',
    acceptedVariants: ['houseplant'],
    box: { x: 0.72, y: 0.55, width: 0.18, height: 0.3 },
  },
  {
    english: 'clock',
    thaiMeaning: 'นาฬิกาแขวน',
    exampleSentence: 'The clock on the wall is slow.',
    cefrLevel: 'A1',
    acceptedVariants: [],
    box: { x: 0.78, y: 0.08, width: 0.12, height: 0.12 },
  },
];

/** Two items, so the analysis fails the "at least three words" rule (FR-024). */
const ALMOST_NOTHING = LIVING_ROOM.slice(0, 2);

/**
 * Output that looks like a provider having a bad day: an empty word, a meaning with
 * no Thai in it, a box outside the photo, and a sentence that never uses the word.
 * Every item is refused, so the analysis fails as invalid output (FR-023, AIR-003).
 */
const NONSENSE: ExtractedItem[] = [
  {
    english: '',
    thaiMeaning: 'โซฟา',
    exampleSentence: 'We sit here.',
    cefrLevel: 'A1',
    acceptedVariants: [],
    box: { x: 0.1, y: 0.1, width: 0.2, height: 0.2 },
  },
  {
    english: 'lamp',
    thaiMeaning: 'lamp',
    exampleSentence: 'The lamp is bright.',
    cefrLevel: 'A1',
    acceptedVariants: [],
    box: { x: 0.1, y: 0.1, width: 0.2, height: 0.2 },
  },
  {
    english: 'window',
    thaiMeaning: 'หน้าต่าง',
    exampleSentence: 'The room is bright.',
    cefrLevel: 'Z9',
    acceptedVariants: [],
    box: { x: 0.9, y: 0.9, width: 0.5, height: 0.5 },
  },
];

/**
 * The AI provider used in development and in tests (B4). It answers at once, costs
 * nothing, and can be told to take any path the real one might: a blocked photo, a
 * provider error, a timeout, too few words, or output that fails every rule.
 *
 * Fixed answers also make the rest of the pipeline testable: the same photo always
 * gives the same words, so a test can assert what a learner ends up with.
 */
@Injectable()
export class FakeAiProvider extends AiProvider {
  readonly name = 'fake';

  modelFor(purpose: AiPurpose): string {
    return `fake-${purpose.toLowerCase()}`;
  }

  /** What every call does unless a test asks for something else. */
  behaviour: FakeBehaviour = 'ALLOWED';

  /**
   * How long an answer takes. Zero keeps tests instant; `AI_FAKE_DELAY_MS` sets it
   * to a few seconds so a developer can see the waiting page as a learner does,
   * since a real analysis takes about 20 seconds (NFR-001).
   */
  delayMs = 0;

  /** Used once, then forgotten: for a test that needs one call to differ. */
  private next: FakeBehaviour | null = null;

  /**
   * What this provider was asked, in order, with the size of each photo — enough
   * for a test to check that the prepared photo was sent rather than the upload.
   */
  readonly calls: {
    purpose: 'SAFETY_CHECK' | 'EXTRACTION' | 'TUTOR';
    bytes: number;
  }[] = [];

  behaveOnce(behaviour: FakeBehaviour): void {
    this.next = behaviour;
  }

  /*
   * A failure comes back as a rejected promise rather than a thrown error, exactly
   * as a real provider's would: a caller may reasonably use `.catch` instead of
   * try/catch, and a fake that threw first would let that mistake through.
   */
  checkPhoto(photo: PhotoForAi): Promise<AiAnswer<SafetyVerdict>> {
    this.calls.push({ purpose: 'SAFETY_CHECK', bytes: photo.data.byteLength });
    const behaviour = this.take();
    const failure = this.failureFor(behaviour);

    return this.after(() =>
      failure
        ? Promise.reject(failure)
        : Promise.resolve({
            value:
              behaviour === 'BLOCKED'
                ? { allowed: false, category: this.blockCategory }
                : { allowed: true },
            // A made-up but plausible cost, so the numbers recorded are not empty.
            usage: { inputTokens: 780, outputTokens: 12 },
          }),
    );
  }

  extractVocabulary(photo: PhotoForAi): Promise<AiAnswer<ExtractedItem[]>> {
    this.calls.push({ purpose: 'EXTRACTION', bytes: photo.data.byteLength });
    const behaviour = this.take();
    const failure = this.failureFor(behaviour);

    const value =
      behaviour === 'TOO_FEW_WORDS'
        ? ALMOST_NOTHING
        : behaviour === 'INVALID_OUTPUT'
          ? NONSENSE
          : LIVING_ROOM;
    return this.after(() =>
      failure
        ? Promise.reject(failure)
        : Promise.resolve({
            value,
            usage: { inputTokens: 820, outputTokens: 40 * value.length },
          }),
    );
  }

  /**
   * A tutor reply, in pieces, as a real one arrives (NFR-003).
   *
   * It looks up the learner's weak words whenever it is asked anything, which is
   * how the tool path gets exercised without a key: everything a real tutor does
   * to the database, this does too. What it says is fixed, because a fake that
   * invented answers would teach a test nothing.
   */
  async *tutorReply(turn: TutorTurn): AsyncIterable<TutorEvent> {
    this.calls.push({ purpose: 'TUTOR', bytes: turn.message.length });
    this.lastHistory = turn.history;
    const behaviour = this.take();
    const failure = this.failureFor(behaviour);
    if (failure) throw failure;

    const weak = turn.tools.find((tool) => tool.name === 'weak_words');
    // The tool result is data about the learner; it is never acted upon here.
    this.lastToolResult = weak ? await weak.run({ count: 3 }) : null;

    for (const piece of [
      'เข้าใจแล้วค่ะ ',
      'คำนี้ใช้บอกสิ่งของในห้อง ',
      'ตัวอย่างเช่น "The sofa is soft."',
    ]) {
      if (this.delayMs > 0) {
        await new Promise((resolve) => setTimeout(resolve, this.delayMs));
      }
      yield { type: 'delta', text: piece };
    }

    yield { type: 'done', usage: { inputTokens: 400, outputTokens: 60 } };
  }

  /**
   * Which rule a blocked photo breaks (FR-091). Suspected illegal material
   * suspends AI at once rather than on the third photo (FR-098), so a test has
   * to be able to ask for that one.
   */
  blockCategory: BlockCategory = 'VIOLENCE';

  /** What the last reply looked up, which a test reads to check the tool path. */
  lastToolResult: unknown = null;

  /** What the last reply was reminded of, which a test reads to check the history. */
  lastHistory: readonly TutorExchange[] = [];

  /**
   * Answers after `delayMs`, so a developer can watch the page wait (FR-021). The
   * answer is made only once the wait is over: a rejected promise left lying around
   * for a few seconds would be reported as unhandled before anyone could catch it.
   */
  private after<T>(answer: () => Promise<T>): Promise<T> {
    return this.delayMs > 0
      ? new Promise<void>((resolve) => setTimeout(resolve, this.delayMs)).then(
          answer,
        )
      : answer();
  }

  /**
   * The behaviour a call uses. A one-off set with `behaveOnce` is consumed here, so
   * the next call falls back to the standing behaviour — which is how a test makes
   * only the safety check fail, or only the extraction.
   */
  private take(): FakeBehaviour {
    const behaviour = this.next ?? this.behaviour;
    this.next = null;
    return behaviour;
  }

  private failureFor(behaviour: FakeBehaviour): AiProviderError | null {
    if (behaviour === 'PROVIDER_ERROR') {
      return new AiProviderError(
        'PROVIDER_ERROR',
        'The fake provider refused.',
      );
    }
    if (behaviour === 'TIMED_OUT') {
      return new AiProviderError('TIMED_OUT', 'The fake provider timed out.');
    }
    return null;
  }
}
