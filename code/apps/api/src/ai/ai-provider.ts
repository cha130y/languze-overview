import type { AiPurpose, BlockCategory } from '../generated/prisma/enums.js';
import type { AiUsage } from './ai-call-recorder.js';
import type { ExtractedItem } from '../vocabulary/extraction-rules.js';

/** A prepared photo on its way to a provider: at most 2,048 pixels (FR-017, V18). */
export interface PhotoForAi {
  data: Buffer;
  contentType: string;
}

/**
 * What the safety check decided (FR-091, FR-092). A refusal by the provider's own
 * safety filters counts as a block, with `OTHER` when no category fits (ADR-0004).
 */
export type SafetyVerdict =
  { allowed: true } | { allowed: false; category: BlockCategory };

/**
 * An answer and what it cost. The usage travels with the answer so that the caller
 * can record it without asking the provider afterwards, which would be a second
 * question with a racy answer (AIR-007).
 */
export interface AiAnswer<T> {
  value: T;
  usage?: AiUsage;
}

/**
 * Why a provider call could not produce an answer. This is never a block: a failure
 * is LanguZe's or the provider's, so the analysis fails and is not counted (FR-025).
 */
export class AiProviderError extends Error {
  constructor(
    readonly code: 'PROVIDER_ERROR' | 'TIMED_OUT' | 'INVALID_OUTPUT',
    message: string,
    options?: { cause?: unknown },
  ) {
    super(message, options);
    this.name = 'AiProviderError';
  }
}

/** One parameter a tutor tool takes. Two shapes are all three tools need. */
export interface ToolParameter {
  name: string;
  type: 'integer' | 'string';
  description: string;
}

/**
 * Something the tutor may look up (FR-072). LanguZe builds these already bound to
 * the signed-in learner, so no tool takes a learner identifier and the model has
 * no way to name one: whose data is read is settled before the model sees a tool.
 *
 * Tools only read. Nothing here can change a learner's words, mastery, or XP.
 */
export interface TutorTool {
  readonly name: string;
  readonly description: string;
  readonly parameters: readonly ToolParameter[];
  /**
   * Answers the model's question. Input from the model is untrusted, so each tool
   * checks it and answers a bad one with an explanation rather than with data.
   */
  run(input: Record<string, unknown>): Promise<unknown>;
}

/** What the learner and the tutor have said so far, oldest first (FR-070). */
export interface TutorExchange {
  role: 'LEARNER' | 'TUTOR';
  content: string;
}

/** One turn of the conversation, and what the tutor may look up during it. */
export interface TutorTurn {
  history: readonly TutorExchange[];
  message: string;
  tools: readonly TutorTool[];
}

/**
 * The reply as it arrives (NFR-003). A learner waiting for a paragraph should see
 * it being written rather than a spinner, so the reply is streamed rather than
 * returned whole, and the cost comes at the end with the last piece (AIR-007).
 */
export type TutorEvent =
  { type: 'delta'; text: string } | { type: 'done'; usage?: AiUsage };

/**
 * What LanguZe asks an AI provider to do (AIR-001). Business rules depend on this
 * interface and never on a provider's SDK, so a provider change is a new adapter
 * rather than a rewrite. The abstract class is also the injection token.
 *
 * The two photo jobs are separate calls on purpose: a photo is classified before it
 * is described, so a blocked photo never reaches extraction (FR-092, ADR-0004).
 */
export abstract class AiProvider {
  /** The provider's name, as the call records show it (AIR-007). */
  abstract readonly name: string;

  /** Which model answers one kind of question; they differ per job (ADR-0004). */
  abstract modelFor(purpose: AiPurpose): string;

  /** Whether this photo may be used at all (FR-091, FR-092). */
  abstract checkPhoto(photo: PhotoForAi): Promise<AiAnswer<SafetyVerdict>>;

  /**
   * The objects in the photo, in the provider's own order of confidence. Whatever
   * it returns is untrusted input: the caller checks every item (AIR-003, FR-023).
   */
  abstract extractVocabulary(
    photo: PhotoForAi,
  ): Promise<AiAnswer<ExtractedItem[]>>;

  /**
   * The tutor's reply, piece by piece (FR-070, FR-073, NFR-003). The provider runs
   * the tool conversation itself, calling the tools LanguZe handed it — so the
   * protocol stays the provider's business while the data stays LanguZe's.
   *
   * Whatever comes back is text for a learner to read, never an instruction: a
   * reply is shown, and nothing in it is acted upon.
   */
  abstract tutorReply(turn: TutorTurn): AsyncIterable<TutorEvent>;
}
