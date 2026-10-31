import { render, screen } from '@testing-library/react';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import type { Account, Usage, WorldSummary } from '@/lib/api/client';
import { getAccount, getUsage, getWorlds } from '@/lib/api/server';
import WorldsPage from './page';

vi.mock('@/lib/api/server', () => ({
  getAccount: vi.fn(),
  getUsage: vi.fn(),
  getWorlds: vi.fn(),
}));

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { worldStatus: vi.fn(() => new Promise(() => {})) },
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

const usage: Usage = {
  analysesLeft: 7,
  analysesLimit: 10,
  messagesLeft: 30,
  messagesLimit: 30,
  resetsAt: '2026-09-25T17:00:00.000Z',
};

const world: WorldSummary = {
  id: 'world-1',
  name: 'ห้องครัว',
  status: 'READY',
  failureReason: null,
  thumbnailUrl: 'https://photos.test/thumb.jpg?X-Amz-Signature=abc',
  wordCount: 12,
  masteredCount: 3,
  createdAt: '2026-09-24T10:00:00.000Z',
};

beforeEach(() => {
  vi.mocked(getAccount).mockReset().mockResolvedValue(account);
  vi.mocked(getWorlds).mockReset().mockResolvedValue([]);
  vi.mocked(getUsage).mockReset().mockResolvedValue(usage);
  redirect.mockClear();
});

test('shows each world with its photo and counts (FR-013)', async () => {
  vi.mocked(getWorlds).mockResolvedValue([
    world,
    {
      ...world,
      id: 'world-2',
      name: 'โต๊ะทำงาน',
      status: 'ANALYZING',
      // A world still being analysed has no words yet.
      wordCount: 0,
      masteredCount: 0,
    },
  ]);

  render(await WorldsPage());

  expect(screen.getByRole('heading', { name: 'ห้องครัว' })).toBeInTheDocument();
  expect(screen.getByText('12 คำ · จำได้แล้ว 3 คำ')).toBeInTheDocument();
  expect(screen.getByText('พร้อมเล่น')).toBeInTheDocument();
  expect(screen.getByText('กำลังวิเคราะห์…')).toBeInTheDocument();
  // The whole card is one link, so its name carries the status and the counts too,
  // which is what a screen reader should read out (NFR-012).
  expect(screen.getByRole('link', { name: /ห้องครัว/ })).toHaveAttribute(
    'href',
    '/worlds/world-1',
  );
});

/** US-011 criterion 3: an empty list explains how to start. */
test('invites a learner with no worlds to make one', async () => {
  render(await WorldsPage());

  expect(screen.getByText('ยังไม่มีโลกของคุณ')).toBeInTheDocument();
  expect(
    screen.getByRole('link', { name: 'สร้างโลกแรกของคุณ' }),
  ).toHaveAttribute('href', '/worlds/new');
});

/** The same story: an unverified learner is told what to do first (FR-006). */
test('asks an unverified learner to verify before creating a world', async () => {
  vi.mocked(getAccount).mockResolvedValue({
    ...account,
    emailVerified: false,
    verifiedForAi: false,
    aiAccess: {
      available: false,
      reason: 'NOT_VERIFIED',
      until: null,
      awaitingReview: false,
    },
  });

  render(await WorldsPage());

  expect(screen.getByText('ยืนยันอีเมลก่อนสร้างโลก')).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'สร้างโลกใหม่' })).toBeNull();
});

/*
 * US-092 criterion 3. The create button goes with it: offering an action the
 * API would refuse is worse than explaining why it is not there.
 */
test('explains a pause instead of offering a new world', async () => {
  vi.mocked(getAccount).mockResolvedValue({
    ...account,
    aiAccess: {
      available: false,
      reason: 'AI_SUSPENDED',
      until: '2026-10-10T09:00:00.000Z',
      awaitingReview: false,
    },
  });

  render(await WorldsPage());

  expect(screen.getByText('ฟีเจอร์ AI ถูกพักไว้ชั่วคราว')).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'สร้างโลกใหม่' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'สร้างโลกใหม่' })).toBeNull();
  // A count of analyses nobody can spend would only mislead.
  expect(screen.queryByText(/วิเคราะห์รูปได้อีก/)).toBeNull();
  // Nor should the empty state invite them to a page that would refuse them.
  expect(screen.queryByRole('link', { name: 'สร้างโลกแรกของคุณ' })).toBeNull();
});

/* The pause is the reason, so the verification notice must not argue with it. */
test('does not also ask a paused learner to verify', async () => {
  vi.mocked(getAccount).mockResolvedValue({
    ...account,
    verifiedForAi: false,
    aiAccess: {
      available: false,
      reason: 'AI_SUSPENDED',
      until: '2026-10-10T09:00:00.000Z',
      awaitingReview: false,
    },
  });

  render(await WorldsPage());

  expect(screen.getByText('ฟีเจอร์ AI ถูกพักไว้ชั่วคราว')).toBeInTheDocument();
  expect(screen.queryByText('ยืนยันอีเมลก่อนสร้างโลก')).toBeNull();
});

test('sends a visitor to sign in', async () => {
  vi.mocked(getAccount).mockResolvedValue(null);

  await expect(WorldsPage()).rejects.toThrow('redirected to /sign-in');
});

/** US-080 criterion 1: how many analyses are left today, before one is spent. */
test('shows what is left of today’s analyses', async () => {
  render(await WorldsPage());

  expect(
    screen.getByText('วันนี้วิเคราะห์รูปได้อีก 7 จาก 10 ครั้ง'),
  ).toBeInTheDocument();
  expect(
    screen.getByRole('link', { name: 'สร้างโลกใหม่' }),
  ).toBeInTheDocument();
});

/** US-080 criterion 2: with none left, creating a world is closed, with the reset. */
test('closes creation when today’s analyses are used up', async () => {
  vi.mocked(getUsage).mockResolvedValue({ ...usage, analysesLeft: 0 });

  render(await WorldsPage());

  expect(screen.queryByRole('link', { name: 'สร้างโลกใหม่' })).toBeNull();
  expect(screen.getByRole('button', { name: 'สร้างโลกใหม่' })).toBeDisabled();
  expect(screen.getByText(/เริ่มนับใหม่ 26 ก.ย. 00:00/)).toBeInTheDocument();
});

/* A count the API could not give must not lock a learner out of their own app. */
test('still offers creation when the count is unknown', async () => {
  vi.mocked(getUsage).mockResolvedValue(null);

  render(await WorldsPage());

  expect(
    screen.getByRole('link', { name: 'สร้างโลกใหม่' }),
  ).toBeInTheDocument();
});

/* NFR-012: the list of worlds, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(await WorldsPage());
  await expectNoAxeViolations(container, { page: true });
});

/* WCAG 3.2.3: every page of the home menu leads back to it the same way. */
test('leads back to the home page', async () => {
  render(await WorldsPage());

  expect(screen.getByRole('link', { name: '← หน้าแรก' })).toHaveAttribute(
    'href',
    '/',
  );
});
