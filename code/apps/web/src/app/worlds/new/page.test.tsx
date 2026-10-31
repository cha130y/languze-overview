import { render, screen } from '@testing-library/react';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import type { Account, Usage } from '@/lib/api/client';
import { getAccount, getUsage } from '@/lib/api/server';
import NewWorldPage from './page';

vi.mock('@/lib/api/server', () => ({
  getAccount: vi.fn(),
  getUsage: vi.fn(),
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
  analysesLeft: 3,
  analysesLimit: 10,
  messagesLeft: 30,
  messagesLimit: 30,
  resetsAt: '2026-09-25T17:00:00.000Z',
};

beforeEach(() => {
  vi.mocked(getAccount).mockReset().mockResolvedValue(account);
  vi.mocked(getUsage).mockReset().mockResolvedValue(usage);
  redirect.mockClear();
});

test('offers the form with what is left of today (US-010, US-080)', async () => {
  render(await NewWorldPage());

  expect(screen.getByLabelText('ชื่อโลก')).toBeInTheDocument();
  expect(
    screen.getByText('วันนี้วิเคราะห์รูปได้อีก 3 จาก 10 ครั้ง'),
  ).toBeInTheDocument();
});

/*
 * US-080 criterion 2. Offering the form would mean refusing the photo only after a
 * learner had uploaded it over a phone connection.
 */
test('does not ask for a photo it would have to refuse', async () => {
  vi.mocked(getUsage).mockResolvedValue({ ...usage, analysesLeft: 0 });

  render(await NewWorldPage());

  expect(screen.queryByLabelText('ชื่อโลก')).toBeNull();
  expect(screen.getByText('วันนี้วิเคราะห์ครบแล้ว')).toBeInTheDocument();
  expect(screen.getByText(/26 ก.ย. 00:00/)).toBeInTheDocument();
});

/** FR-006: analysis is an AI feature, so it waits for a verified email. */
/* Told here rather than sent away: this is the page the explanation answers. */
test('explains a pause instead of asking for a photo', async () => {
  vi.mocked(getAccount).mockResolvedValue({
    ...account,
    aiAccess: {
      available: false,
      reason: 'AI_SUSPENDED',
      until: '2026-10-10T09:00:00.000Z',
      awaitingReview: false,
    },
  });

  render(await NewWorldPage());

  expect(screen.getByText('ฟีเจอร์ AI ถูกพักไว้ชั่วคราว')).toBeInTheDocument();
  expect(screen.queryByLabelText(/ชื่อโลก/)).toBeNull();
  // The subtitle would otherwise invite exactly what the panel refuses.
  expect(
    screen.queryByText('ตั้งชื่อสถานที่ แล้วใส่รูปภาพหนึ่งรูป'),
  ).toBeNull();
});

test('sends an unverified learner back to the list', async () => {
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

  await expect(NewWorldPage()).rejects.toThrow('redirected to /worlds');
});

/* NFR-012: the new world form, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(await NewWorldPage());
  await expectNoAxeViolations(container, { page: true });
});
