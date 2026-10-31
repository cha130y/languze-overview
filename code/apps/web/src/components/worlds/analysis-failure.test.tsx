import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { ApiError, api, type World } from '@/lib/api/client';
import { AnalysisFailure } from './analysis-failure';

const { refresh } = vi.hoisted(() => ({ refresh: vi.fn() }));
vi.mock('next/navigation', () => {
  const router = { refresh, push: vi.fn() };
  return { useRouter: () => router };
});

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { retryAnalysis: vi.fn() },
}));

const failed = (overrides: Partial<World> = {}) =>
  ({
    id: 'world-1',
    name: 'ห้องครัว',
    status: 'FAILED',
    failureReason: 'TOO_FEW_WORDS',
    photoUrl: 'https://photos.test/photo.jpg?X-Amz-Signature=abc',
    thumbnailUrl: null,
    wordCount: 0,
    masteredCount: 0,
    createdAt: '2026-09-24T10:00:00.000Z',
    words: [],
    ...overrides,
  }) as World;

beforeEach(() => {
  refresh.mockReset();
  vi.mocked(api.retryAnalysis).mockReset().mockResolvedValue(undefined);
});

/** US-021 criterion 1: too few words comes with advice about the photo. */
test('says what went wrong and what to do about it', () => {
  render(<AnalysisFailure world={failed()} />);

  expect(screen.getByText(/อย่างน้อย 3 คำ/)).toBeInTheDocument();
});

/** US-021 criterion 4, US-080 criterion 6: a failure of ours is not their quota. */
test('says a failed analysis did not use today’s quota', () => {
  render(
    <AnalysisFailure world={failed({ failureReason: 'PROVIDER_ERROR' })} />,
  );

  expect(screen.getByText(/ไม่ถูกนับในโควตา/)).toBeInTheDocument();
});

test('analyses the same photo again on request (US-021)', async () => {
  render(<AnalysisFailure world={failed()} />);

  await userEvent.click(
    screen.getByRole('button', { name: 'วิเคราะห์รูปเดิมอีกครั้ง' }),
  );

  expect(api.retryAnalysis).toHaveBeenCalledWith('world-1');
  expect(refresh).toHaveBeenCalled();
});

/*
 * FR-092: a blocked photo is deleted, so there is nothing to analyse again. The API
 * refuses the retry; offering the button anyway would only produce an error.
 */
test('offers no retry for a blocked photo, only a new world', () => {
  render(
    <AnalysisFailure
      world={failed({ failureReason: 'BLOCKED', photoUrl: null })}
    />,
  );

  expect(
    screen.queryByRole('button', { name: 'วิเคราะห์รูปเดิมอีกครั้ง' }),
  ).toBeNull();
  expect(screen.getByText(/ลบโลกนี้แล้วสร้างใหม่/)).toBeInTheDocument();
  // FR-093: this one did use a slot, and the learner should know why.
  expect(screen.getByText(/นับรวมในโควตา/)).toBeInTheDocument();
  expect(
    screen.getByRole('link', { name: 'สร้างโลกใหม่ด้วยรูปอื่น' }),
  ).toHaveAttribute('href', '/worlds/new');
});

/* US-091 criterion 2: the message stays general, and points at the rules. */
test('points a blocked photo at the Terms of Use', () => {
  render(
    <AnalysisFailure
      world={failed({ failureReason: 'BLOCKED', photoUrl: null })}
    />,
  );

  expect(
    screen.getByRole('link', { name: 'ข้อกำหนดการใช้งาน' }),
  ).toHaveAttribute('href', '/terms-of-use');
});

test('sends no other failure to the rules, since it broke none', () => {
  render(<AnalysisFailure world={failed()} />);

  expect(screen.queryByRole('link', { name: 'ข้อกำหนดการใช้งาน' })).toBeNull();
});

test('explains a refused retry and leaves the world as it was', async () => {
  vi.mocked(api.retryAnalysis).mockRejectedValue(
    new ApiError('DAILY_ANALYSIS_LIMIT', 429, 'no analyses left'),
  );
  render(<AnalysisFailure world={failed()} />);

  await userEvent.click(
    screen.getByRole('button', { name: 'วิเคราะห์รูปเดิมอีกครั้ง' }),
  );

  expect(await screen.findByRole('alert')).toHaveTextContent(
    /ใช้การวิเคราะห์รูปภาพครบแล้ว/,
  );
  expect(refresh).not.toHaveBeenCalled();
});
