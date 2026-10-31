import { describe, expect, it } from 'vitest';
import type { BlockCategory } from '../generated/prisma/enums.js';
import {
  BLOCKS_BEFORE_SUSPENSION,
  BLOCK_WINDOW_DAYS,
  LEVEL_ONE_DAYS,
  awaitsReview,
  blockWindowFrom,
  currentSuspension,
  isInForce,
  suspensionFor,
  type SuspensionRecord,
} from './suspension-rules.js';

const NOW = new Date('2026-10-02T09:00:00Z');
const DAY_MS = 24 * 60 * 60 * 1000;
const days = (count: number) => new Date(NOW.getTime() + count * DAY_MS);

const blocked = (
  blocksInWindow: number,
  options: { category?: BlockCategory; suspendedBefore?: boolean } = {},
) =>
  suspensionFor(
    {
      category: options.category ?? 'SEXUAL_CONTENT',
      blocksInWindow,
      suspendedBefore: options.suspendedBefore ?? false,
    },
    NOW,
  );

/* V15: 3 blocked photos within 7 days, and what happens the second time. */
describe('what a blocked photo earns', () => {
  it('earns nothing below the threshold', () => {
    for (let count = 0; count < BLOCKS_BEFORE_SUSPENSION; count += 1) {
      expect(blocked(count)).toBeNull();
    }
  });

  it('pauses AI for a week the first time the threshold is reached', () => {
    const raised = blocked(BLOCKS_BEFORE_SUSPENSION);

    expect(raised?.reason).toBe('REPEATED_BLOCKS');
    expect(raised?.endsAt).toEqual(days(LEVEL_ONE_DAYS));
  });

  /*
   * Level 2 waits for a person rather than a date: a learner who has already
   * served a pause and come back to the same threshold is no longer a case the
   * clock can settle (V15).
   */
  it('waits for an admin when it has happened before', () => {
    const raised = blocked(BLOCKS_BEFORE_SUSPENSION, {
      suspendedBefore: true,
    });

    expect(raised?.reason).toBe('REPEATED_AFTER_SUSPENSION');
    expect(raised?.endsAt).toBeNull();
  });

  it('treats more than the threshold the same as the threshold', () => {
    expect(blocked(BLOCKS_BEFORE_SUSPENSION + 5)?.reason).toBe(
      'REPEATED_BLOCKS',
    );
  });

  /*
   * FR-098. The count is about a pattern; this is about one photo, so it does
   * not wait for a third and it does not end by itself.
   */
  describe('suspected illegal material', () => {
    it('suspends at once, on the very first one', () => {
      const raised = blocked(1, { category: 'SUSPECTED_ILLEGAL_MATERIAL' });

      expect(raised?.reason).toBe('SUSPECTED_ILLEGAL_MATERIAL');
      expect(raised?.endsAt).toBeNull();
    });

    it('still waits for an admin when the learner has been suspended before', () => {
      const raised = blocked(BLOCKS_BEFORE_SUSPENSION, {
        category: 'SUSPECTED_ILLEGAL_MATERIAL',
        suspendedBefore: true,
      });

      expect(raised?.reason).toBe('SUSPECTED_ILLEGAL_MATERIAL');
      expect(raised?.endsAt).toBeNull();
    });
  });

  it('counts the window back seven days from now', () => {
    expect(blockWindowFrom(NOW)).toEqual(days(-BLOCK_WINDOW_DAYS));
  });
});

const record = (
  overrides: Partial<SuspensionRecord> = {},
): SuspensionRecord => ({
  reason: 'REPEATED_BLOCKS',
  startsAt: days(-1),
  endsAt: days(6),
  liftedAt: null,
  ...overrides,
});

/* The status is worked out from the records, so nothing has to run to end one. */
describe('the suspension a learner is under', () => {
  it('is nothing when there are no records', () => {
    expect(currentSuspension([], NOW)).toBeNull();
  });

  it('is the one that has started and not yet ended', () => {
    expect(currentSuspension([record()], NOW)?.reason).toBe('REPEATED_BLOCKS');
  });

  /* No scheduled job ends a suspension; it simply stops counting (V15). */
  it('is nothing once the end time has passed', () => {
    expect(currentSuspension([record({ endsAt: days(-1) })], NOW)).toBeNull();
  });

  it('is nothing at the exact moment it ends', () => {
    expect(currentSuspension([record({ endsAt: NOW })], NOW)).toBeNull();
  });

  it('is nothing once an admin has lifted it', () => {
    expect(
      currentSuspension([record({ liftedAt: days(-1), endsAt: null })], NOW),
    ).toBeNull();
  });

  it('never ends by itself when it waits for an admin', () => {
    const waiting = record({ endsAt: null, startsAt: days(-400) });

    expect(currentSuspension([waiting], NOW)).toBe(waiting);
  });

  /* History is kept, so old records must not be mistaken for the current one. */
  it('is the newest of several, ignoring the ones that are over', () => {
    const old = record({ startsAt: days(-30), endsAt: days(-23) });
    const lifted = record({ startsAt: days(-10), liftedAt: days(-9) });
    const now = record({
      startsAt: days(-2),
      endsAt: null,
      reason: 'REPEATED_AFTER_SUSPENSION',
    });

    expect(currentSuspension([old, now, lifted], NOW)).toBe(now);
  });

  it('picks the latest by when it started, whatever order they arrive in', () => {
    const earlier = record({ startsAt: days(-5) });
    const later = record({
      startsAt: days(-1),
      reason: 'SUSPECTED_ILLEGAL_MATERIAL',
      endsAt: null,
    });

    expect(currentSuspension([later, earlier], NOW)).toBe(later);
    expect(currentSuspension([earlier, later], NOW)).toBe(later);
  });
});

describe('whether a case needs a person', () => {
  it('does when it has no end time (FR-103)', () => {
    expect(awaitsReview(record({ endsAt: null }))).toBe(true);
  });

  it('does not when a date will end it', () => {
    expect(awaitsReview(record({ endsAt: days(6) }))).toBe(false);
  });
});

/* The definition shared by what a learner is under and what an admin acts on. */
describe('whether a suspension is in force', () => {
  it('is while it waits for review', () => {
    expect(isInForce(record({ endsAt: null }), NOW)).toBe(true);
  });

  it('is until its end date', () => {
    expect(isInForce(record({ endsAt: days(1) }), NOW)).toBe(true);
  });

  /* The end is the moment AI comes back, not the last moment it is off. */
  it('is not at its end date, nor after it', () => {
    expect(isInForce(record({ endsAt: NOW }), NOW)).toBe(false);
    expect(isInForce(record({ endsAt: days(-1) }), NOW)).toBe(false);
  });

  it('is not once lifted, however long it had left', () => {
    expect(isInForce(record({ endsAt: null, liftedAt: days(-1) }), NOW)).toBe(
      false,
    );
    expect(
      isInForce(record({ endsAt: days(5), liftedAt: days(-1) }), NOW),
    ).toBe(false);
  });
});
