import { render, screen } from '@testing-library/react';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import type { Account, TutorConversation, Usage } from '@/lib/api/client';
import { getAccount, getTutorConversation, getUsage } from '@/lib/api/server';
import TutorPage from './page';

vi.mock('@/lib/api/server', () => ({
  getAccount: vi.fn(),
  getTutorConversation: vi.fn(),
  getUsage: vi.fn(),
}));

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: {
    tutorConversation: vi.fn(),
    clearTutorConversation: vi.fn(),
    sendVerificationEmail: vi.fn(),
  },
}));

vi.mock('@/lib/api/tutor-stream', () => ({ sendTutorMessage: vi.fn() }));

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

const account = (overrides: Partial<Account> = {}): Account => ({
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
  ...overrides,
});

const conversation: TutorConversation = {
  messages: [
    {
      id: '1',
      role: 'LEARNER',
      content: 'sofa แปลว่าอะไร',
      createdAt: '2026-10-02T04:00:00.000Z',
    },
  ],
  nextCursor: null,
};

const usage = {
  analysesLeft: 10,
  analysesLimit: 10,
  messagesLeft: 30,
  messagesLimit: 30,
  resetsAt: '2026-10-02T17:00:00.000Z',
} as Usage;

beforeEach(() => {
  vi.mocked(getAccount).mockReset().mockResolvedValue(account());
  vi.mocked(getTutorConversation).mockReset().mockResolvedValue(conversation);
  vi.mocked(getUsage).mockReset().mockResolvedValue(usage);
  redirect.mockClear();
  Element.prototype.scrollIntoView = vi.fn();
});

test('shows the conversation and somewhere to ask (US-070)', async () => {
  render(await TutorPage());

  expect(screen.getByText('sofa แปลว่าอะไร')).toBeInTheDocument();
  expect(screen.getByLabelText('ข้อความถึงติวเตอร์')).toBeInTheDocument();
});

/*
 * FR-006: the API refuses an unverified learner, which is what enforces the rule.
 * The page only avoids offering a box that could never be answered.
 */
test('shows an unverified learner how to verify instead of a box', async () => {
  vi.mocked(getAccount).mockResolvedValue(
    account({ emailVerified: false, verifiedForAi: false }),
  );

  render(await TutorPage());

  expect(screen.getByText('ยืนยันอีเมลก่อนใช้ติวเตอร์ AI')).toBeInTheDocument();
  expect(screen.queryByLabelText('ข้อความถึงติวเตอร์')).toBeNull();
});

/* An account with no email address has nothing to resend to (D1, FR-009). */
test('does not offer to resend when there is no address', async () => {
  vi.mocked(getAccount).mockResolvedValue(
    account({ email: null, emailVerified: false, verifiedForAi: false }),
  );

  render(await TutorPage());

  expect(
    screen.getByText('ติวเตอร์ AI ใช้ได้หลังยืนยันบัญชีของคุณ'),
  ).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /ส่งลิงก์/ })).toBeNull();
});

/* The API being unreachable must leave a learner able to ask, not a crash. */
test('still offers the box when the conversation could not be read', async () => {
  vi.mocked(getTutorConversation).mockResolvedValue(null);
  vi.mocked(getUsage).mockResolvedValue(null);

  render(await TutorPage());

  expect(screen.getByLabelText('ข้อความถึงติวเตอร์')).toBeInTheDocument();
  expect(screen.getByText('ถามติวเตอร์ได้เลย')).toBeInTheDocument();
});

/* US-092 criterion 3: the explanation belongs on the page they opened. */
test('explains a pause instead of offering the box', async () => {
  vi.mocked(getAccount).mockResolvedValue(
    account({
      aiAccess: {
        available: false,
        reason: 'AI_SUSPENDED',
        until: '2026-10-10T09:00:00.000Z',
        awaitingReview: false,
      },
    }),
  );

  render(await TutorPage());

  expect(screen.getByText('ฟีเจอร์ AI ถูกพักไว้ชั่วคราว')).toBeInTheDocument();
  expect(screen.queryByLabelText('ข้อความถึงติวเตอร์')).toBeNull();
  // The subtitle would otherwise promise exactly what the panel refuses.
  expect(screen.queryByText(/ติวเตอร์ตอบเป็นภาษาไทย/)).toBeNull();
});

test('sends a visitor to sign in', async () => {
  vi.mocked(getAccount).mockResolvedValue(null);

  await expect(TutorPage()).rejects.toThrow('redirected to /sign-in');
});

test('sends an account that has not accepted the Terms to accept them', async () => {
  vi.mocked(getAccount).mockResolvedValue(account({ termsAccepted: false }));

  await expect(TutorPage()).rejects.toThrow('redirected to /terms');
});

/* NFR-012: the tutor page, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(await TutorPage());
  await expectNoAxeViolations(container, { page: true });
});
