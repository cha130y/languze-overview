import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { ApiError, api, type World } from '@/lib/api/client';
import { GIVE_UP_MS, POLL_MS } from '@/lib/use-analysis-watch';
import { AnalysisProgress } from './analysis-progress';

const { refresh } = vi.hoisted(() => ({ refresh: vi.fn() }));
// Next hands back the same router object on every render, and the watcher relies on
// that: a new one each time would restart its clock and it would never give up.
vi.mock('next/navigation', () => {
  const router = { refresh, push: vi.fn() };
  return { useRouter: () => router };
});

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { worldStatus: vi.fn() },
}));

const world = {
  id: 'world-1',
  name: 'ห้องครัว',
  status: 'ANALYZING',
  failureReason: null,
  photoUrl: 'https://photos.test/photo.jpg?X-Amz-Signature=abc',
  thumbnailUrl: null,
  wordCount: 0,
  masteredCount: 0,
  createdAt: '2026-09-24T10:00:00.000Z',
  words: [],
} as World;

const stillAnalysing = { status: 'ANALYZING', failureReason: null } as const;

beforeEach(() => {
  vi.useFakeTimers();
  refresh.mockReset();
  vi.mocked(api.worldStatus).mockReset().mockResolvedValue(stillAnalysing);
});

afterEach(() => {
  vi.useRealTimers();
});

/** Timers and the answers they set off, settled together. */
const wait = (ms: number = POLL_MS) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

test('says the work is happening elsewhere (FR-021, US-020)', () => {
  render(<AnalysisProgress world={world} />);

  expect(screen.getByText('กำลังวิเคราะห์รูปภาพ…')).toBeInTheDocument();
  expect(screen.getByText(/ออกจากหน้านี้/)).toBeInTheDocument();
  expect(screen.getByAltText('รูปภาพของ ห้องครัว')).toBeInTheDocument();
});

/** P3: every 3 seconds, and only while the world is still being analysed. */
test('asks the API every 3 seconds', async () => {
  render(<AnalysisProgress world={world} />);

  expect(api.worldStatus).not.toHaveBeenCalled();
  await wait(POLL_MS * 3);

  expect(api.worldStatus).toHaveBeenCalledTimes(3);
  expect(api.worldStatus).toHaveBeenCalledWith('world-1');
  expect(refresh).not.toHaveBeenCalled();
});

test('refreshes the page as soon as the analysis finishes', async () => {
  vi.mocked(api.worldStatus)
    .mockResolvedValueOnce(stillAnalysing)
    .mockResolvedValue({ status: 'READY', failureReason: null });

  render(<AnalysisProgress world={world} />);
  await wait(POLL_MS * 2);

  expect(refresh).toHaveBeenCalled();
});

test('stops asking when the learner leaves the page (P3)', async () => {
  const { unmount } = render(<AnalysisProgress world={world} />);
  await wait();
  expect(api.worldStatus).toHaveBeenCalledTimes(1);

  unmount();
  await wait(POLL_MS * 5);

  expect(api.worldStatus).toHaveBeenCalledTimes(1);
});

/* An API that cannot be reached has not said the analysis failed, only nothing. */
test('keeps waiting when a question goes unanswered', async () => {
  vi.mocked(api.worldStatus).mockRejectedValue(
    new ApiError('NETWORK_ERROR', 0, 'unreachable'),
  );

  render(<AnalysisProgress world={world} />);
  await wait(POLL_MS * 2);

  expect(refresh).not.toHaveBeenCalled();
  expect(screen.getByText('กำลังวิเคราะห์รูปภาพ…')).toBeInTheDocument();
});

/* Without this the page would spin for ever against an API that never answers. */
test('gives up and asks the learner to refresh', async () => {
  render(<AnalysisProgress world={world} />);
  await wait(GIVE_UP_MS + POLL_MS);
  const asked = vi.mocked(api.worldStatus).mock.calls.length;

  expect(screen.getByText('ยังไม่ได้ผลลัพธ์')).toBeInTheDocument();

  await wait(POLL_MS * 5);
  expect(api.worldStatus).toHaveBeenCalledTimes(asked);
});
