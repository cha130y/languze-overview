import { HttpStatus, Injectable } from '@nestjs/common';
import { AiCallRecorder } from '../ai/ai-call-recorder.js';
import { AiProvider, AiProviderError } from '../ai/ai-provider.js';
import { AuthService } from '../auth/auth.service.js';
import { SuspensionService } from '../moderation/suspension.service.js';
import { AppError } from '../platform/errors/app-error.js';
import { ErrorCode } from '../platform/errors/error-codes.js';
import { TutorToolsService } from './tutor-tools.service.js';
import { MAX_MESSAGE_LENGTH, TutorService } from './tutor.service.js';

/** A message that has passed every check and already holds one of today's. */
export interface TutorTurnInProgress {
  learnerId: string;
  content: string;
  /** The day the message was taken from, so the right one is given back (V1). */
  day: Date;
  messagesLeft: number;
}

/** What the learner's browser receives, one frame at a time (API design 3.6). */
export type ReplyEvent =
  | { event: 'delta'; data: { text: string } }
  | {
      event: 'done';
      data: { messageId: string; replyId: string; messagesLeft: number };
    };

/**
 * Answering a tutor message (FR-070–FR-075, NFR-003).
 *
 * Split in two on purpose. `begin` does everything that may refuse the message,
 * while the response is still an ordinary one that can carry a JSON error; only
 * then does the endpoint open the stream, after which a failure can only be an
 * `error` event. The two halves are why a learner who is out of messages gets a
 * plain `429` rather than a stream that immediately says sorry.
 */
@Injectable()
export class TutorReplyService {
  constructor(
    private readonly tutor: TutorService,
    private readonly tools: TutorToolsService,
    private readonly ai: AiProvider,
    private readonly recorder: AiCallRecorder,
    private readonly auth: AuthService,
    private readonly suspensions: SuspensionService,
  ) {}

  /**
   * Checks the message and takes one of today's (FR-006, FR-071, V7). Everything
   * here can still be reported as a normal error, so everything that can refuse
   * belongs here rather than after the stream has started.
   */
  async begin(
    learnerId: string,
    content: string,
  ): Promise<TutorTurnInProgress> {
    if (content.length > MAX_MESSAGE_LENGTH) {
      throw new AppError(
        ErrorCode.MESSAGE_TOO_LONG,
        HttpStatus.BAD_REQUEST,
        `A message can be at most ${MAX_MESSAGE_LENGTH} characters.`,
        { limit: MAX_MESSAGE_LENGTH },
      );
    }

    // The tutor is an AI feature, so it needs a verified account whose AI
    // features are not paused (FR-006, FR-096).
    await this.suspensions.requireAccess(
      learnerId,
      await this.auth.isVerifiedForAi(learnerId),
      'Verify your email address to ask the tutor.',
    );

    const { day, messagesLeft } = await this.tutor.reserve(learnerId);
    return { learnerId, content, day, messagesLeft };
  }

  /**
   * The reply, piece by piece, saved once it is whole.
   *
   * Nothing is written to the conversation until the provider has finished: a
   * half-answer is not a reply, and a learner who saw one would have paid a
   * message for it. Any failure gives the message back instead (FR-071, S6).
   */
  async *stream(turn: TutorTurnInProgress): AsyncIterable<ReplyEvent> {
    let done: ReplyEvent;
    let reply = '';

    try {
      const events = this.recorder.stream(
        {
          purpose: 'TUTOR',
          provider: this.ai.name,
          model: this.ai.modelFor('TUTOR'),
        },
        this.ai.tutorReply({
          history: await this.tutor.remembered(turn.learnerId),
          message: turn.content,
          tools: this.tools.forLearner(turn.learnerId),
        }),
        // The cost arrives with the last event of the turn (AIR-007).
        (event) => (event.type === 'done' ? event.usage : undefined),
      );

      for await (const event of events) {
        if (event.type !== 'delta') continue;
        reply += event.text;
        yield { event: 'delta', data: { text: event.text } };
      }

      /*
       * A provider that streamed nothing has not answered. Saving it would put an
       * empty message in the conversation and charge the learner for it, so it is
       * a failure like any other.
       */
      if (reply.trim() === '') {
        throw new AiProviderError(
          'INVALID_OUTPUT',
          'The tutor gave no reply to save.',
        );
      }

      const saved = await this.tutor.remember(
        turn.learnerId,
        turn.content,
        reply,
      );
      done = {
        event: 'done',
        data: { ...saved, messagesLeft: turn.messagesLeft },
      };
    } catch (error) {
      await this.tutor.release(turn.learnerId, turn.day);
      throw error;
    }

    /*
     * Outside the try: by now the message is saved and counted, and a consumer
     * that has stopped listening must not undo either of those.
     */
    yield done;
  }
}

/** The code the browser is told a failed reply by (API design, section 3.6). */
export function replyErrorCode(error: unknown): ErrorCode {
  if (error instanceof AppError) return error.code;
  if (error instanceof AiProviderError)
    return ErrorCode.AI_PROVIDER_UNAVAILABLE;
  return ErrorCode.INTERNAL_ERROR;
}
