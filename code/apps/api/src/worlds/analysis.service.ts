import { HttpStatus, Injectable, Logger } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { AiCallRecorder } from '../ai/ai-call-recorder.js';
import { AiProvider, AiProviderError } from '../ai/ai-provider.js';
import type { EnvironmentVariables } from '../config/env.validation.js';
import type {
  AnalysisFailure,
  BlockCategory,
} from '../generated/prisma/enums.js';
import { ReviewNotifierService } from '../moderation/review-notifier.service.js';
import { SuspensionService } from '../moderation/suspension.service.js';
import { AppError } from '../platform/errors/app-error.js';
import { ErrorCode } from '../platform/errors/error-codes.js';
import { startOfBangkokDay } from '../platform/time/bangkok-day.js';
import { PrismaService } from '../prisma/prisma.service.js';
import { PhotoCleanupService } from '../storage/photo-cleanup.service.js';
import { PhotoStorage } from '../storage/photo-storage.js';
import {
  MIN_WORDS_PER_WORLD,
  checkedItems,
  type CheckedItem,
} from '../vocabulary/extraction-rules.js';
import { StaleAnalysisService } from './stale-analysis.service.js';

/** Statuses that have used one of the learner's analyses for today (FR-020, U2). */
const COUNTED = ['IN_PROGRESS', 'SUCCEEDED', 'BLOCKED'] as const;

/**
 * Turns a world's photo into its words (FR-020–FR-025).
 *
 * The work runs after the answer has been sent, because a learner should not wait
 * for an AI provider (P2, FR-021). What that background run may do is therefore
 * written down carefully: it owns the analysis row it created, it checks that the
 * world still exists before saving anything, and every ending — words, a block, or
 * a failure — is one transaction.
 */
@Injectable()
export class AnalysisService {
  private readonly logger = new Logger(AnalysisService.name);
  private readonly dailyLimit: number;

  /** Runs in flight, so tests and shutdown can wait for one to finish. */
  private readonly running = new Map<string, Promise<void>>();

  constructor(
    private readonly prisma: PrismaService,
    private readonly storage: PhotoStorage,
    private readonly ai: AiProvider,
    private readonly recorder: AiCallRecorder,
    private readonly suspensions: SuspensionService,
    private readonly review: ReviewNotifierService,
    private readonly stale: StaleAnalysisService,
    private readonly photoCleanup: PhotoCleanupService,
    config: ConfigService<EnvironmentVariables, true>,
  ) {
    this.dailyLimit = config.get('DAILY_ANALYSIS_LIMIT', { infer: true });
  }

  /**
   * Takes one of today's analyses, or refuses (FR-020). This happens before the
   * photo is stored, so a learner who has none left ends up with nothing half
   * created — and before the answer is sent, so they are told at once.
   */
  take(learnerId: string, worldId?: string): Promise<string> {
    return this.claim(learnerId, worldId);
  }

  /** Attaches a taken analysis to its world and starts the work (P2). */
  async begin(
    analysisId: string,
    worldId: string,
    storageKey: string,
  ): Promise<void> {
    await this.prisma.analysis.update({
      where: { id: analysisId },
      data: { worldId },
    });
    this.runInBackground(analysisId, worldId, storageKey);
  }

  /**
   * Gives a taken analysis back when the world it was for could not be created.
   * It is released rather than counted, because nothing was analysed (FR-025).
   */
  async release(analysisId: string): Promise<void> {
    await this.prisma.analysis.update({
      where: { id: analysisId },
      data: {
        status: 'FAILED',
        failureReason: 'PROVIDER_ERROR',
        finishedAt: new Date(),
      },
    });
  }

  /**
   * Runs the analysis of the same photo again (FR-025). Only a failed world with a
   * photo can be retried: a blocked photo is gone, so its world can only be deleted.
   * Access to AI features is checked before this, by `WorldsService.retry`.
   */
  async retry(learnerId: string, worldId: string): Promise<void> {
    const world = await this.prisma.world.findFirst({
      where: { id: worldId, learnerId },
      include: { photo: true },
    });

    if (!world) {
      throw new AppError(
        ErrorCode.NOT_FOUND,
        HttpStatus.NOT_FOUND,
        'This world does not exist.',
      );
    }
    if (world.status !== 'FAILED' || !world.photo) {
      throw new AppError(
        ErrorCode.RETRY_NOT_AVAILABLE,
        HttpStatus.CONFLICT,
        world.photo
          ? 'This world has nothing to retry.'
          : 'This photo was removed, so the world can only be deleted.',
      );
    }

    const analysisId = await this.claim(learnerId, worldId);
    await this.prisma.world.update({
      where: { id: worldId },
      data: { status: 'ANALYZING', failureReason: null },
    });
    this.runInBackground(analysisId, worldId, world.photo.storageKey);
  }

  /** How many analyses are left today, and when the count starts again (FR-080). */
  async usage(learnerId: string, now = new Date()) {
    const used = await this.prisma.analysis.count({
      where: {
        learnerId,
        startedAt: { gte: startOfBangkokDay(now) },
        status: { in: [...COUNTED] },
      },
    });
    return {
      used,
      limit: this.dailyLimit,
      left: Math.max(0, this.dailyLimit - used),
    };
  }

  /** Waits for a background run, which tests use instead of polling. */
  settled(worldId: string): Promise<void> {
    return this.running.get(worldId) ?? Promise.resolve();
  }

  /**
   * Counts today's analyses and takes one, both inside a transaction holding a lock
   * on this learner. Without the lock two uploads sent at once could each see nine
   * used and both take the tenth (U2).
   */
  private async claim(learnerId: string, worldId?: string): Promise<string> {
    const analysisId = await this.prisma.$transaction(async (tx) => {
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${learnerId}, 0))`;

      const used = await tx.analysis.count({
        where: {
          learnerId,
          startedAt: { gte: startOfBangkokDay() },
          status: { in: [...COUNTED] },
        },
      });
      if (used >= this.dailyLimit) {
        throw new AppError(
          ErrorCode.DAILY_ANALYSIS_LIMIT,
          HttpStatus.TOO_MANY_REQUESTS,
          `You have used all ${this.dailyLimit} photo analyses for today.`,
          { limit: this.dailyLimit },
        );
      }

      const analysis = await tx.analysis.create({
        data: { learnerId, worldId: worldId ?? null },
      });
      return analysis.id;
    });
    // Every analysis starts here, so this is where the timeout check learns of it (V16).
    this.stale.expect();
    return analysisId;
  }

  private runInBackground(
    analysisId: string,
    worldId: string,
    storageKey: string,
  ): void {
    const run = this.run(analysisId, worldId, storageKey)
      .catch(async (error: unknown) => {
        /*
         * Nobody is waiting for an answer any more, so a failure here has to be
         * written down rather than thrown: the learner sees the world's status.
         * Anything unexpected — storage unreachable, a bug — is LanguZe's fault
         * and is released like a provider error (FR-025).
         */
        const reason = reasonFor(error);
        if (!(error instanceof AnalysisFailed)) {
          this.logger.error(
            `Analysis ${analysisId} ended badly`,
            error instanceof Error ? error.stack : String(error),
          );
        }
        await this.failed(analysisId, worldId, reason).catch(
          (failureError: unknown) => {
            this.logger.error(
              `Analysis ${analysisId} could not be marked failed`,
              failureError instanceof Error
                ? failureError.stack
                : String(failureError),
            );
          },
        );
      })
      .finally(() => {
        this.running.delete(worldId);
      });

    this.running.set(worldId, run);
  }

  private async run(
    analysisId: string,
    worldId: string,
    storageKey: string,
  ): Promise<void> {
    const photo = {
      data: await this.storage.read(storageKey),
      contentType: 'image/jpeg',
    };

    // A provider that cannot answer is not a block: the learner did nothing wrong.
    const verdict = await this.recorder.record(
      {
        purpose: 'SAFETY_CHECK',
        provider: this.ai.name,
        model: this.ai.modelFor('SAFETY_CHECK'),
      },
      () => this.ai.checkPhoto(photo),
    );

    if (!verdict.allowed) {
      await this.blocked(analysisId, worldId, verdict.category);
      return;
    }

    const extracted = await this.recorder.record(
      {
        purpose: 'EXTRACTION',
        provider: this.ai.name,
        model: this.ai.modelFor('EXTRACTION'),
      },
      () => this.ai.extractVocabulary(photo),
    );
    const items = checkedItems(extracted);

    if (items.length === 0) {
      throw new AnalysisFailed('INVALID_OUTPUT');
    }
    if (items.length < MIN_WORDS_PER_WORLD) {
      throw new AnalysisFailed('TOO_FEW_WORDS');
    }

    await this.save(analysisId, worldId, items);
  }

  /** The words a learner gets, the world, and the analysis: all or nothing. */
  private async save(
    analysisId: string,
    worldId: string,
    items: CheckedItem[],
  ): Promise<void> {
    await this.prisma.$transaction(async (tx) => {
      const world = await tx.world.findUnique({ where: { id: worldId } });
      // The learner deleted the world while this ran, so the result is dropped.
      if (!world) {
        await tx.analysis.update({
          where: { id: analysisId },
          data: { status: 'SUCCEEDED', finishedAt: new Date() },
        });
        return;
      }

      for (const item of items) {
        const word = await tx.vocabularyWord.upsert({
          where: {
            learnerId_english_thaiMeaningKey: {
              learnerId: world.learnerId,
              english: item.english,
              thaiMeaningKey: item.thaiMeaningKey,
            },
          },
          create: {
            learnerId: world.learnerId,
            english: item.english,
            thaiMeaning: item.thaiMeaning,
            thaiMeaningKey: item.thaiMeaningKey,
          },
          update: {},
        });

        await tx.wordOccurrence.create({
          data: {
            worldId,
            vocabularyWordId: word.id,
            boxX: item.box.x,
            boxY: item.box.y,
            boxWidth: item.box.width,
            boxHeight: item.box.height,
            exampleSentence: item.exampleSentence,
            cefrLevel: item.cefrLevel,
            acceptedVariants: item.acceptedVariants,
          },
        });
      }

      await tx.world.update({
        where: { id: worldId },
        data: { status: 'READY', failureReason: null },
      });
      await tx.analysis.update({
        where: { id: analysisId },
        data: { status: 'SUCCEEDED', finishedAt: new Date() },
      });
    });
  }

  /**
   * A photo that broke the rules (FR-092–FR-095). It is recorded, its world keeps
   * no photo, and the file is removed from storage straight after the transaction;
   * if that fails, the cleanup record keeps it on the list.
   */
  private async blocked(
    analysisId: string,
    worldId: string,
    category: BlockCategory,
  ): Promise<void> {
    const world = await this.prisma.world.findUnique({
      where: { id: worldId },
      include: { photo: true, thumbnail: true },
    });
    if (!world) return;

    const photos = [world.photo, world.thumbnail].filter(
      (photo) => photo !== null,
    );

    const raised = await this.prisma.$transaction(async (tx) => {
      await tx.photoBlock.create({
        data: { learnerId: world.learnerId, category },
      });
      /*
       * The pause this block has earned, decided here so the count it reads
       * already includes this photo and two blocks landing together cannot each
       * decide they were the second (FR-096, FR-098, moderation flow section 1).
       */
      const suspension = await this.suspensions.raiseFor(
        tx,
        world.learnerId,
        category,
      );
      await tx.photoDeletion.createMany({
        data: photos.map((photo) => ({
          storageKey: photo.storageKey,
          reason: 'PHOTO_BLOCKED' as const,
        })),
      });
      await tx.storedPhoto.deleteMany({
        where: { id: { in: photos.map((photo) => photo.id) } },
      });
      await tx.world.update({
        where: { id: worldId },
        data: { status: 'FAILED', failureReason: 'BLOCKED' },
      });
      // A blocked photo counts toward the daily limit (FR-093).
      await tx.analysis.update({
        where: { id: analysisId },
        data: {
          status: 'BLOCKED',
          failureReason: 'BLOCKED',
          finishedAt: new Date(),
        },
      });

      return suspension;
    });

    /*
     * After the commit, never inside it: a slow mail server must not hold a
     * transaction open, and an email about a pause that was then rolled back
     * would be worse than no email at all (FR-107, moderation flow section 1).
     */
    if (raised) await this.review.caseNeedsReview(raised);

    // The records stay until the cleanup task has confirmed each file is gone.
    this.photoCleanup.expect();
    await Promise.all(
      photos.map((photo) =>
        this.storage.remove(photo.storageKey).catch(() => {
          // The cleanup record already has it; the task will try again.
        }),
      ),
    );
  }

  /** A failure of LanguZe or its provider: the analysis is released (FR-025). */
  private async failed(
    analysisId: string,
    worldId: string,
    reason: AnalysisFailure,
  ): Promise<void> {
    await this.prisma.$transaction([
      this.prisma.world.updateMany({
        where: { id: worldId },
        data: { status: 'FAILED', failureReason: reason },
      }),
      this.prisma.analysis.update({
        where: { id: analysisId },
        data: {
          status: 'FAILED',
          failureReason: reason,
          finishedAt: new Date(),
        },
      }),
    ]);
  }
}

/** Carries why an analysis failed, so every ending is handled in one place. */
export class AnalysisFailed extends Error {
  constructor(readonly reason: AnalysisFailure) {
    super(`The analysis failed: ${reason}`);
  }
}

/**
 * What to record for an error. A provider's own words are kept — a timeout is a
 * timeout — and anything else is a provider error, which is released (FR-025).
 */
function reasonFor(error: unknown): AnalysisFailure {
  if (error instanceof AnalysisFailed) return error.reason;
  if (error instanceof AiProviderError) {
    if (error.code === 'TIMED_OUT') return 'TIMED_OUT';
    if (error.code === 'INVALID_OUTPUT') return 'INVALID_OUTPUT';
  }
  return 'PROVIDER_ERROR';
}
