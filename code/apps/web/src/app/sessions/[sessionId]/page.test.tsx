import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import {
  api,
  type Account,
  type AnswerResult,
  type PracticeSession,
} from '@/lib/api/client';
import { getAccount, getSession } from '@/lib/api/server';
import SessionPage from './page';

vi.mock('@/lib/api/server', () => ({
  getAccount: vi.fn(),
  getSession: vi.fn(),
}));

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { answerQuestion: vi.fn(), startGame: vi.fn() },
}));

const { redirect, notFound } = vi.hoisted(() => ({
  redirect: vi.fn((path: string) => {
    throw new Error(`redirected to ${path}`);
  }),
  notFound: vi.fn(() => {
    throw new Error('not found');
  }),
}));
vi.mock('next/navigation', () => {
  const router = { refresh: vi.fn(), push: vi.fn() };
  return { redirect, notFound, useRouter: () => router };
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

const session = (overrides: Partial<PracticeSession> = {}) =>
  ({
    id: 'session-1',
    kind: 'GAME',
    status: 'IN_PROGRESS',
    worldId: 'world-1',
    answeredCount: 0,
    questionCount: 4,
    nextQuestion: {
      id: 'question-1',
      position: 1,
      photoUrl: 'https://photos.test/photo.jpg?X-Amz-Signature=abc',
      box: { x: 0.2, y: 0.1, width: 0.4, height: 0.25 },
    },
    startedAt: '2026-09-25T04:00:00.000Z',
    summary: null,
    ...overrides,
  }) as PracticeSession;

const params = Promise.resolve({ sessionId: 'session-1' });

beforeEach(() => {
  vi.mocked(getAccount).mockReset().mockResolvedValue(account);
  vi.mocked(getSession).mockReset().mockResolvedValue(session());
  vi.mocked(api.answerQuestion)
    .mockReset()
    .mockResolvedValue({
      correct: false,
      dontKnow: true,
      alreadyAnswered: false,
      answer: null,
      matchedVariant: false,
      word: {
        english: 'sofa',
        thaiMeaning: 'โซฟา',
        exampleSentence: 'We sit on the sofa.',
      },
      xpAwarded: 0,
      mastery: { before: null, after: 'LEARNING' },
      sessionCompleted: false,
      summary: null,
    } as AnswerResult);
  redirect.mockClear();
  notFound.mockClear();
});

test('asks the next question (FR-031)', async () => {
  render(await SessionPage({ params }));

  expect(screen.getByText('ข้อ 1 จาก 4')).toBeInTheDocument();
  expect(screen.getByLabelText(/ภาษาอังกฤษเรียกว่าอะไร/)).toBeInTheDocument();
});

/* US-034 criterion 2: a reload lands on the next unanswered question. */
test('continues where the learner left off', async () => {
  vi.mocked(getSession).mockResolvedValue(
    session({
      answeredCount: 2,
      nextQuestion: {
        id: 'question-3',
        position: 3,
        photoUrl: 'https://photos.test/photo.jpg',
        box: { x: 0.1, y: 0.1, width: 0.2, height: 0.2 },
      },
    }),
  );

  render(await SessionPage({ params }));

  expect(screen.getByText('ข้อ 3 จาก 4')).toBeInTheDocument();
});

test('shows the result of a finished game (US-033)', async () => {
  vi.mocked(getSession).mockResolvedValue(
    session({
      status: 'COMPLETED',
      answeredCount: 4,
      nextQuestion: null,
      summary: {
        answeredCount: 4,
        correctCount: 3,
        xpEarned: 30,
        levelChanges: [{ english: 'sofa', level: 'FAMILIAR' }],
      },
    }),
  );

  render(await SessionPage({ params }));

  expect(screen.getByText('ตอบถูก 3 จาก 4 ข้อ')).toBeInTheDocument();
  expect(screen.getByText('ได้รับ 30 XP')).toBeInTheDocument();
  expect(screen.getByText('sofa')).toBeInTheDocument();
  expect(screen.getByText('เริ่มคุ้น')).toBeInTheDocument();
});

/* FR-036: a session closed unfinished has no summary, and says why. */
test('explains an abandoned game instead of showing a summary', async () => {
  vi.mocked(getSession).mockResolvedValue(
    session({ status: 'ABANDONED', nextQuestion: null }),
  );

  render(await SessionPage({ params }));

  expect(screen.getByText('เกมนี้จบไปแล้ว')).toBeInTheDocument();
  expect(screen.queryByText(/ตอบถูก/)).toBeNull();
  expect(
    screen.getByRole('link', { name: 'กลับไปเริ่มเกมใหม่' }),
  ).toHaveAttribute('href', '/worlds/world-1');
});

test('shows nothing for a session that is not the learner’s', async () => {
  vi.mocked(getSession).mockResolvedValue(null);

  await expect(SessionPage({ params })).rejects.toThrow('not found');
});

test('sends a visitor to sign in', async () => {
  vi.mocked(getAccount).mockResolvedValue(null);

  await expect(SessionPage({ params })).rejects.toThrow(
    'redirected to /sign-in',
  );
});

/*
 * Found in a browser, not in a test: React keeps a client component's state when
 * the server component around it re-renders, so without a key on the question the
 * next one arrived still wearing the last one's feedback, with no answer box.
 */
test('starts each question clean, with no trace of the last answer', async () => {
  const { rerender } = render(await SessionPage({ params }));
  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));
  expect(
    await screen.findByRole('button', { name: 'ข้อต่อไป' }),
  ).toBeInTheDocument();

  // The server has moved on, which is what router.refresh() brings back.
  vi.mocked(getSession).mockResolvedValue(
    session({
      answeredCount: 1,
      nextQuestion: {
        id: 'question-2',
        position: 2,
        photoUrl: 'https://photos.test/photo.jpg',
        box: { x: 0.5, y: 0.5, width: 0.2, height: 0.2 },
      },
    }),
  );
  rerender(await SessionPage({ params }));

  expect(screen.getByText('ข้อ 2 จาก 4')).toBeInTheDocument();
  expect(screen.getByLabelText(/ภาษาอังกฤษ/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'ข้อต่อไป' })).toBeNull();
  expect(screen.queryByText('sofa')).toBeNull();
});

/* NFR-012: a question in a game, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(await SessionPage({ params }));
  await expectNoAxeViolations(container, { page: true });
});
