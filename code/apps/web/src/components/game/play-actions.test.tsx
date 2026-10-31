import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { ApiError, api, type PracticeSession } from '@/lib/api/client';
import { PlayActions } from './play-actions';

const { push, refresh } = vi.hoisted(() => ({
  push: vi.fn(),
  refresh: vi.fn(),
}));
vi.mock('next/navigation', () => {
  const router = { push, refresh };
  return { useRouter: () => router };
});

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { startGame: vi.fn(), startReview: vi.fn() },
}));

const openGame = {
  id: 'session-1',
  kind: 'GAME',
  status: 'IN_PROGRESS',
  worldId: 'world-1',
  answeredCount: 3,
  questionCount: 8,
  startedAt: '2026-09-25T04:00:00.000Z',
  nextQuestion: null,
  summary: null,
} as PracticeSession;

beforeEach(() => {
  push.mockReset();
  refresh.mockReset();
  vi.mocked(api.startGame)
    .mockReset()
    .mockResolvedValue({ ...openGame, id: 'session-2' });
});

test('starts a game and goes to it (US-030)', async () => {
  render(<PlayActions kind="GAME" worldId="world-1" openSession={null} />);

  await userEvent.click(screen.getByRole('button', { name: 'เริ่มเล่นเกม' }));

  expect(api.startGame).toHaveBeenCalledWith('world-1');
  expect(push).toHaveBeenCalledWith('/sessions/session-2');
});

/*
 * US-034 criterion 3. Continuing is a link, not a request: the session is already
 * there, and the page it leads to knows which question comes next.
 */
test('offers the unfinished game first, with where it left off', () => {
  render(<PlayActions kind="GAME" worldId="world-1" openSession={openGame} />);

  expect(
    screen.getByRole('link', { name: 'เล่นต่อ (ข้อ 4 จาก 8)' }),
  ).toHaveAttribute('href', '/sessions/session-1');
  expect(
    screen.getByRole('button', { name: 'เริ่มเกมใหม่' }),
  ).toBeInTheDocument();
});

/* FR-036: a new game closes the old one, so the learner is told before pressing. */
test('says what starting again would cost', () => {
  render(<PlayActions kind="GAME" worldId="world-1" openSession={openGame} />);

  expect(screen.getByText(/ปิดเกมที่ค้างอยู่/)).toBeInTheDocument();
});

test('says nothing about closing a game when none is open', () => {
  render(<PlayActions kind="GAME" worldId="world-1" openSession={null} />);

  expect(screen.queryByText(/ปิดเกมที่ค้างอยู่/)).toBeNull();
  expect(screen.queryByRole('link')).toBeNull();
});

test('explains a refusal and stays put', async () => {
  vi.mocked(api.startGame).mockRejectedValue(
    new ApiError('WORLD_NOT_READY', 409, 'not ready'),
  );
  render(<PlayActions kind="GAME" worldId="world-1" openSession={null} />);

  await userEvent.click(screen.getByRole('button', { name: 'เริ่มเล่นเกม' }));

  expect(await screen.findByRole('alert')).toHaveTextContent(/ยังไม่พร้อมเล่น/);
  expect(push).not.toHaveBeenCalled();
});

/*
 * A review is the same job in another voice: it draws from every world, so it
 * names none, and the wording says "review" rather than "game".
 */
test('starts a review, which names no world (US-050)', async () => {
  vi.mocked(api.startReview).mockResolvedValue({
    ...openGame,
    id: 'review-1',
    kind: 'REVIEW',
    worldId: null,
  });
  render(<PlayActions kind="REVIEW" openSession={null} />);

  await userEvent.click(screen.getByRole('button', { name: 'เริ่มทบทวน' }));

  expect(api.startReview).toHaveBeenCalled();
  expect(api.startGame).not.toHaveBeenCalled();
  expect(push).toHaveBeenCalledWith('/sessions/review-1');
});

test('offers an unfinished review back first', () => {
  render(
    <PlayActions
      kind="REVIEW"
      openSession={{ ...openGame, kind: 'REVIEW', worldId: null }}
    />,
  );

  expect(
    screen.getByRole('link', { name: 'เล่นต่อ (ข้อ 4 จาก 8)' }),
  ).toBeInTheDocument();
  expect(screen.getByText(/ปิดรอบที่ค้างอยู่/)).toBeInTheDocument();
});

/* FR-053: nothing to review is explained, not shown as a failure. */
test('explains when there is nothing to review', async () => {
  vi.mocked(api.startReview).mockRejectedValue(
    new ApiError('NOTHING_TO_REVIEW', 409, 'nothing'),
  );
  render(<PlayActions kind="REVIEW" openSession={null} />);

  await userEvent.click(screen.getByRole('button', { name: 'เริ่มทบทวน' }));

  expect(await screen.findByRole('alert')).toHaveTextContent(
    /ยังไม่มีคำศัพท์ให้ทบทวน/,
  );
});
