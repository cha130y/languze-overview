import { render, screen } from '@testing-library/react';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import type { Account, Progress, World } from '@/lib/api/client';
import { getAccount, getProgress, getWorld } from '@/lib/api/server';
import ProgressPage from './page';

vi.mock('@/lib/api/server', () => ({
  getAccount: vi.fn(),
  requireLearner: (account: { termsAccepted: boolean } | null) => {
    if (!account) throw new Error('redirected to /sign-in');
    if (!account.termsAccepted) throw new Error('redirected to /terms');
  },
  getProgress: vi.fn(),
  getWorld: vi.fn(),
}));

const redirect = vi.fn((path: string) => {
  throw new Error(`redirected to ${path}`);
});
vi.mock('next/navigation', () => ({
  redirect: (path: string) => redirect(path),
}));

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

const progress: Progress = {
  totalXp: 240,
  words: { NEW: 4, LEARNING: 3, FAMILIAR: 2, MASTERED: 1 },
  worlds: [
    { id: 'world-1', name: 'ห้องครัว', wordCount: 6, masteredCount: 1 },
    { id: 'world-2', name: 'โต๊ะทำงาน', wordCount: 4, masteredCount: 0 },
  ],
};

const world: World = {
  id: 'world-1',
  name: 'ห้องครัว',
  status: 'READY',
  wordCount: 1,
  masteredCount: 1,
  createdAt: '2026-01-01T00:00:00Z',
  words: [
    {
      id: 'word-1',
      english: 'kettle',
      thaiMeaning: 'กาต้มน้ำ',
      exampleSentence: 'A kettle is on the table.',
      cefrLevel: 'A1',
      box: { x: 0, y: 0, width: 1, height: 1 },
      mastery: 'MASTERED',
    },
  ],
};

beforeEach(() => {
  vi.mocked(getAccount).mockReset().mockResolvedValue(account);
  vi.mocked(getProgress).mockReset().mockResolvedValue(progress);
  vi.mocked(getWorld)
    .mockReset()
    .mockImplementation(async (id) => ({
      ...world,
      id,
      name: id === 'world-2' ? 'โต๊ะทำงาน' : world.name,
    }));
  redirect.mockClear();
});

test('shows the XP, the levels, and each world (US-060)', async () => {
  render(await ProgressPage());

  expect(screen.getByText('240 XP')).toBeInTheDocument();
  expect(screen.getByText('คำศัพท์ของฉัน (10 คำ)')).toBeInTheDocument();
  expect(screen.getByText('ยังไม่ได้ฝึก')).toBeInTheDocument();
  expect(screen.getByText('4 คำ')).toBeInTheDocument();
  expect(screen.getByText('จำได้แล้ว')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /ห้องครัว/ })).toHaveAttribute(
    'href',
    '/worlds/world-1',
  );
  expect(
    screen.getByRole('button', { name: /ห้องครัว: 6 คำ/ }),
  ).toBeInTheDocument();
  expect(screen.getByText('6 คำ · จำได้แล้ว 1 คำ')).toBeInTheDocument();
});

test('invites a learner with nothing yet to make a world', async () => {
  vi.mocked(getProgress).mockResolvedValue({
    totalXp: 0,
    words: { NEW: 0, LEARNING: 0, FAMILIAR: 0, MASTERED: 0 },
    worlds: [],
  });

  render(await ProgressPage());

  expect(screen.getByText('0 XP')).toBeInTheDocument();
  expect(screen.getByText(/ยังไม่มีคำศัพท์/)).toBeInTheDocument();
  expect(screen.getByText('ยังไม่มีโลกของคุณ')).toBeInTheDocument();
});

/* The page must not claim a score it could not read. */
test('says so when the API could not answer', async () => {
  vi.mocked(getProgress).mockResolvedValue(null);

  render(await ProgressPage());

  expect(screen.getByText(/ยังดูความคืบหน้าไม่ได้/)).toBeInTheDocument();
  expect(screen.queryByText(/XP/)).toBeNull();
});

test('sends a visitor to sign in', async () => {
  vi.mocked(getAccount).mockResolvedValue(null);

  await expect(ProgressPage()).rejects.toThrow('redirected to /sign-in');
});

/* NFR-012: the progress page, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(await ProgressPage());
  await expectNoAxeViolations(container, { page: true });
});
