import type {
  BlockCategory,
  SuspensionReason,
} from '../generated/prisma/enums.js';

/**
 * When repeated blocked photos pause a learner's AI features, and for how long
 * (FR-096, FR-098, V15; moderation flow section 1).
 *
 * Nothing here reads or writes: the decision is separated from the transaction
 * that acts on it, because it is the part that has to be right. The thresholds
 * are V15's, named rather than written into the condition that uses them.
 */

/** How many blocked photos reach the threshold, and over what span (V15). */
export const BLOCKS_BEFORE_SUSPENSION = 3;
export const BLOCK_WINDOW_DAYS = 7;

/** How long a first suspension lasts before it lifts itself (V15, level 1). */
export const LEVEL_ONE_DAYS = 7;

const DAY_MS = 24 * 60 * 60 * 1000;

/** The start of the window "3 blocked photos within 7 days" counts over. */
export const blockWindowFrom = (now: Date): Date =>
  new Date(now.getTime() - BLOCK_WINDOW_DAYS * DAY_MS);

/** What a learner's record says at the moment a photo is blocked. */
export interface BlockOutcome {
  category: BlockCategory;
  /** Blocked photos in the last 7 days, this one included. */
  blocksInWindow: number;
  /** Whether this learner has ever been suspended before, lifted or not. */
  suspendedBefore: boolean;
}

/** A suspension to raise: its reason, and when it lifts itself, if ever. */
export interface SuspensionToRaise {
  reason: SuspensionReason;
  /** Empty means "until an admin reviews" (FR-103). */
  endsAt: Date | null;
}

/**
 * The suspension a blocked photo earns, or nothing.
 *
 * Suspected illegal material suspends at once whatever the count, because the
 * count is about a pattern and this is about one photo (FR-098). Everything else
 * waits for the threshold, and whether the learner has been here before decides
 * whether the pause ends by itself or waits for a person (V15).
 */
export function suspensionFor(
  outcome: BlockOutcome,
  now: Date,
): SuspensionToRaise | null {
  if (outcome.category === 'SUSPECTED_ILLEGAL_MATERIAL') {
    return { reason: 'SUSPECTED_ILLEGAL_MATERIAL', endsAt: null };
  }

  if (outcome.blocksInWindow < BLOCKS_BEFORE_SUSPENSION) return null;

  return outcome.suspendedBefore
    ? { reason: 'REPEATED_AFTER_SUSPENSION', endsAt: null }
    : {
        reason: 'REPEATED_BLOCKS',
        endsAt: new Date(now.getTime() + LEVEL_ONE_DAYS * DAY_MS),
      };
}

/** As much of a suspension record as deciding a learner's status needs. */
export interface SuspensionRecord {
  reason: SuspensionReason;
  startsAt: Date;
  endsAt: Date | null;
  liftedAt: Date | null;
}

/**
 * The suspension a learner is under, or nothing (data model, `ai_suspensions`).
 *
 * Worked out from the records rather than stored, so a suspension whose end time
 * has passed needs no scheduled job to end it: it simply stops counting.
 */
export function currentSuspension<T extends SuspensionRecord>(
  all: readonly T[],
  now: Date,
): T | null {
  let current: T | null = null;

  for (const one of all) {
    if (!isInForce(one, now)) continue;
    if (current === null || one.startsAt > current.startsAt) current = one;
  }

  return current;
}

/**
 * Whether a suspension is still keeping AI features off: not lifted, and either
 * waiting for review or not yet at its end.
 *
 * One definition, used both to decide what a learner is under and to decide what
 * an admin's lift or new end date applies to, so the two can never disagree about
 * which suspensions count.
 */
export const isInForce = (suspension: SuspensionRecord, now: Date): boolean =>
  suspension.liftedAt === null &&
  (suspension.endsAt === null || suspension.endsAt > now);

/** Whether a suspension waits for a person rather than for a date (FR-103). */
export const awaitsReview = (suspension: SuspensionRecord): boolean =>
  suspension.endsAt === null;
