import {
  Injectable,
  Logger,
  type OnApplicationBootstrap,
} from '@nestjs/common';
import { Cron, CronExpression } from '@nestjs/schedule';
import { PrismaService } from '../prisma/prisma.service.js';

/** An analysis that has run this long is lost, not slow (V16, FR-021). */
export const ANALYSIS_TIMEOUT_MS = 5 * 60_000;

export const staleBefore = (now: Date): Date =>
  new Date(now.getTime() - ANALYSIS_TIMEOUT_MS);

/** How long the minute check keeps looking after an analysis starts: the timeout, then one more run. */
export const ANALYSIS_WATCH_MS = ANALYSIS_TIMEOUT_MS + 60_000;

/**
 * Releases analyses that never finished (V16).
 *
 * Background work lives in the API process (P2), so a restart in the middle of an
 * analysis leaves a world saying "analysing" for ever and an analysis counted
 * against the learner's day. This runs at startup, which is exactly when that has
 * just happened, and every minute afterwards for a run that hung.
 *
 * The minute check only reads the database while an analysis may still be running.
 * Analyses start in this process alone (A2), which says so through `expect()`, so
 * outside that window there is nothing to release. Reading anyway, every minute,
 * kept the database awake around the clock: Neon never suspended its compute, and
 * the free plan's monthly hours would have run out mid-month (production
 * deployment, section 10).
 */
@Injectable()
export class StaleAnalysisService implements OnApplicationBootstrap {
  private readonly logger = new Logger(StaleAnalysisService.name);

  /** Until when an analysis may still be running, in epoch milliseconds. */
  private watchUntil = 0;

  constructor(private readonly prisma: PrismaService) {}

  /**
   * A restart is the usual reason an analysis is lost, so this runs first. One the
   * previous process started in its last five minutes is not stale yet, so the
   * minute check watches for it too.
   */
  async onApplicationBootstrap(): Promise<void> {
    this.expect();
    await this.release(new Date());
  }

  /** Called when an analysis starts, so the minute check looks until it must have ended. */
  expect(now: Date = new Date()): void {
    this.watchUntil = Math.max(
      this.watchUntil,
      now.getTime() + ANALYSIS_WATCH_MS,
    );
  }

  @Cron(CronExpression.EVERY_MINUTE)
  async releaseLostAnalyses(): Promise<void> {
    const now = new Date();
    if (now.getTime() > this.watchUntil) return;
    await this.release(now);
  }

  /** Returns how many were released, which the tests and the logs use. */
  async release(now: Date): Promise<number> {
    const stale = await this.prisma.analysis.findMany({
      where: { status: 'IN_PROGRESS', startedAt: { lt: staleBefore(now) } },
      select: { id: true, worldId: true },
    });
    if (stale.length === 0) return 0;

    const worldIds = stale
      .map(({ worldId }) => worldId)
      .filter((worldId) => worldId !== null);

    await this.prisma.$transaction([
      this.prisma.analysis.updateMany({
        where: { id: { in: stale.map(({ id }) => id) } },
        // Released, because the learner's photo may have been perfectly good (FR-025).
        data: { status: 'FAILED', failureReason: 'TIMED_OUT', finishedAt: now },
      }),
      this.prisma.world.updateMany({
        // Only a world still waiting: one that finished another way is left alone.
        where: { id: { in: worldIds }, status: 'ANALYZING' },
        data: { status: 'FAILED', failureReason: 'TIMED_OUT' },
      }),
    ]);

    this.logger.log(
      `Released ${stale.length} analysis(es) that never finished`,
    );
    return stale.length;
  }
}
