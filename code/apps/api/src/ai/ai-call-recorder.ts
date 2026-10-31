import { Injectable, Logger } from '@nestjs/common';
import type { AiOutcome, AiPurpose } from '../generated/prisma/enums.js';
import { PrismaService } from '../prisma/prisma.service.js';
import { currentRequestId } from '../platform/request-context/request-context.js';
import { AiProviderError } from './ai-provider.js';

/** What a provider reports about one call, when it reports anything. */
export interface AiUsage {
  inputTokens?: number;
  outputTokens?: number;
}

export interface AiCallDetails {
  purpose: AiPurpose;
  provider: string;
  model: string;
}

/**
 * Records what every AI call cost and how it went (AIR-007, NFR-017).
 *
 * The row holds no learner identifier, no photo, no prompt and no message text —
 * only which job it was, which model answered, how long it took, and the outcome.
 * That is enough to watch cost and reliability without keeping anything private.
 */
@Injectable()
export class AiCallRecorder {
  private readonly logger = new Logger(AiCallRecorder.name);

  constructor(private readonly prisma: PrismaService) {}

  /**
   * Runs one provider call and writes its record either way. The record is written
   * after the call, and a failure to write it is logged rather than thrown: losing
   * a cost record must never turn a good answer into a failed analysis.
   */
  async record<T>(
    details: AiCallDetails,
    call: () => Promise<{ value: T; usage?: AiUsage }>,
  ): Promise<T> {
    const startedAt = new Date();
    const started = performance.now();

    try {
      const { value, usage } = await call();
      await this.write(details, startedAt, started, 'SUCCEEDED', usage);
      return value;
    } catch (error) {
      await this.write(
        details,
        startedAt,
        started,
        outcomeOf(error),
        undefined,
        codeOf(error),
      );
      throw error;
    }
  }

  /**
   * The same record for a streamed call (AIR-007). A stream reports its cost only
   * when it ends, so the record is written once the last event has gone past —
   * and the events are passed through untouched, so nothing waits for it.
   */
  async *stream<T>(
    details: AiCallDetails,
    events: AsyncIterable<T>,
    usageOf: (event: T) => AiUsage | undefined,
  ): AsyncIterable<T> {
    const startedAt = new Date();
    const started = performance.now();
    let usage: AiUsage | undefined;

    try {
      for await (const event of events) {
        usage = usageOf(event) ?? usage;
        yield event;
      }
    } catch (error) {
      await this.write(
        details,
        startedAt,
        started,
        outcomeOf(error),
        undefined,
        codeOf(error),
      );
      throw error;
    }

    await this.write(details, startedAt, started, 'SUCCEEDED', usage);
  }

  private async write(
    details: AiCallDetails,
    startedAt: Date,
    started: number,
    outcome: AiOutcome,
    usage?: AiUsage,
    errorCode?: string,
  ): Promise<void> {
    try {
      await this.prisma.aiCall.create({
        data: {
          ...details,
          startedAt,
          latencyMs: Math.round(performance.now() - started),
          inputTokens: usage?.inputTokens ?? null,
          outputTokens: usage?.outputTokens ?? null,
          outcome,
          errorCode: errorCode ?? null,
          // Ties the call to the request log line that caused it (NFR-016).
          requestId: currentRequestId() ?? null,
        },
      });
    } catch (error) {
      this.logger.warn(
        `Could not record the ${details.purpose} call: ${
          error instanceof Error ? error.message : String(error)
        }`,
      );
    }
  }
}

/** A provider's own words for what went wrong, mapped to the four outcomes. */
function outcomeOf(error: unknown): AiOutcome {
  if (!(error instanceof AiProviderError)) return 'FAILED';
  if (error.code === 'TIMED_OUT') return 'TIMED_OUT';
  if (error.code === 'INVALID_OUTPUT') return 'INVALID_OUTPUT';
  return 'FAILED';
}

function codeOf(error: unknown): string {
  if (error instanceof AiProviderError) return error.code;
  return error instanceof Error ? error.name : 'UNKNOWN';
}
