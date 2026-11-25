import { render, screen } from '@testing-library/react';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import type { Account, PracticeSession, Progress } from '@/lib/api/client';
import { getAccount, getCurrentReview, getProgress } from '@/lib/api/server';
import ReviewPage from './page';

vi.mock('@/lib/api/server', () => ({
  getAccount: vi.fn(),
  requireLearner: (account: { termsAccepted: boolean } | null) => {
    if (!account) throw new Error('redirected to /sign-in');
    if (!account.termsAccepted) throw new Error('redirected to /terms');
  },
  getCurrentReview: vi.fn(),
  getProgress: vi.fn(),
}));

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { startReview: vi.fn(), startGame: vi.fn() },
}));

const redirect = vi.fn((path: string) => {
  throw new Error(`redirected to ${path}`);
});
vi.mock('next/navigation', () => {
  const router = { refresh: vi.fn(), push: vi.fn() };
  return {
    redirect: (path: string) => redirect(path),
    useRouter: () => router,
  };
});

const account: Account = {
  id: 'learner-1',
  name: 'Nok',
  email: 'nok@example.com',
  emailVerified: true,
  verifiedForAi: true,
  role: 'LEARNER',
  termsAccepted: true,
  aiAccess: {
    available: true,
    reason: null,
    until: null,
    awaitingReview: false,
  },
};

const progress = (words: Partial<Progress['words']> = {}): Progress => ({
  totalXp: 0,
  words: { NEW: 0, LEARNING: 0, FAMILIAR: 0, MASTERED: 0, ...words },
  worlds: [],
});

const openReview = {
  id: 'review-1',
  kind: 'REVIEW',
  status: 'IN_PROGRESS',
  worldId: null,
  answeredCount: 2,
  questionCount: 7,
  nextQuestion: null,
  startedAt: '2026-09-25T04:00:00.000Z',
  summary: null,
} as PracticeSession;

beforeEach(() => {
  vi.mocked(getAccount).mockReset().mockResolvedValue(account);
  vi.mocked(getCurrentReview).mockReset().mockResolvedValue(null);
  vi.mocked(getProgress)
    .mockReset()
    .mockResolvedValue(progress({ LEARNING: 3, FAMILIAR: 2 }));
  redirect.mockClear();
});

test('offers a review, and says how much is waiting (US-050)', async () => {
  render(await ReviewPage());

  expect(
    screen.getByText('ตอนนี้มีคำที่ยังจำไม่แม่นอยู่ 5 คำ'),
  ).toBeInTheDocument();
  expect(
    screen.getByRole('button', { name: 'เริ่มทบทวน' }),
  ).toBeInTheDocument();
});

/*
 * US-051: told on arrival, not after pressing a button that could only refuse.
 * A learner's first visit here is the likeliest to have nothing behind it.
 */
test('explains when nothing is waiting, and points somewhere useful', async () => {
  vi.mocked(getProgress).mockResolvedValue(progress({ NEW: 8, MASTERED: 2 }));

  render(await ReviewPage());

  expect(screen.getByText('ยังไม่มีอะไรให้ทบทวน')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'เริ่มทบทวน' })).toBeNull();
  expect(
    screen.getByRole('link', { name: 'ไปเล่นเกมในโลกของฉัน' }),
  ).toHaveAttribute('href', '/worlds');
});

/* A word never answered is not reviewable, however many of them there are. */
test('does not count new or mastered words as waiting', async () => {
  vi.mocked(getProgress).mockResolvedValue(
    progress({ NEW: 40, MASTERED: 12, LEARNING: 1 }),
  );

  render(await ReviewPage());

  expect(
    screen.getByText('ตอนนี้มีคำที่ยังจำไม่แม่นอยู่ 1 คำ'),
  ).toBeInTheDocument();
});

test('offers the unfinished review back first (US-034)', async () => {
  vi.mocked(getCurrentReview).mockResolvedValue(openReview);

  render(await ReviewPage());

  expect(
    screen.getByRole('link', { name: 'เล่นต่อ (ข้อ 3 จาก 7)' }),
  ).toHaveAttribute('href', '/sessions/review-1');
  expect(screen.queryByText('ยังไม่มีอะไรให้ทบทวน')).toBeNull();
});

/* A count nobody could read must not be shown as "nothing to review". */
test('still offers a review when the count is unknown', async () => {
  vi.mocked(getProgress).mockResolvedValue(null);

  render(await ReviewPage());

  expect(
    screen.getByRole('button', { name: 'เริ่มทบทวน' }),
  ).toBeInTheDocument();
  expect(screen.queryByText('ยังไม่มีอะไรให้ทบทวน')).toBeNull();
});

test('sends a visitor to sign in', async () => {
  vi.mocked(getAccount).mockResolvedValue(null);

  await expect(ReviewPage()).rejects.toThrow('redirected to /sign-in');
});

/* NFR-012: the review page, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(await ReviewPage());
  await expectNoAxeViolations(container, { page: true });
});
