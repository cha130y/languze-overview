import { act, render } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { api, type WorldSummary } from '@/lib/api/client';
import { POLL_MS } from '@/lib/use-analysis-watch';
import { AnalysisWatcher } from './analysis-watcher';

const { refresh } = vi.hoisted(() => ({ refresh: vi.fn() }));
vi.mock('next/navigation', () => {
  const router = { refresh, push: vi.fn() };
  return { useRouter: () => router };
});

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { worldStatus: vi.fn() },
}));

const world = (id: string, status: WorldSummary['status']) =>
  ({
    id,
    name: id,
    status,
    failureReason: null,
    thumbnailUrl: null,
    wordCount: 0,
    masteredCount: 0,
    createdAt: '2026-09-24T10:00:00.000Z',
  }) as WorldSummary;

beforeEach(() => {
  vi.useFakeTimers();
  refresh.mockReset();
  vi.mocked(api.worldStatus)
    .mockReset()
    .mockResolvedValue({ status: 'ANALYZING', failureReason: null });
});

afterEach(() => {
  vi.useRealTimers();
});

const wait = () =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(POLL_MS);
  });

test('asks only about the worlds still being analysed (P3)', async () => {
  render(
    <AnalysisWatcher
      worlds={[
        world('ready-1', 'READY'),
        world('busy-1', 'ANALYZING'),
        world('failed-1', 'FAILED'),
      ]}
    />,
  );
  await wait();

  expect(api.worldStatus).toHaveBeenCalledTimes(1);
  expect(api.worldStatus).toHaveBeenCalledWith('busy-1');
});

/* A list of settled worlds must not turn into a request every three seconds. */
test('asks nothing when no analysis is running', async () => {
  render(<AnalysisWatcher worlds={[world('ready-1', 'READY')]} />);
  await wait();

  expect(api.worldStatus).not.toHaveBeenCalled();
});

test('refreshes the list once an analysis finishes', async () => {
  vi.mocked(api.worldStatus).mockResolvedValue({
    status: 'READY',
    failureReason: null,
  });

  render(<AnalysisWatcher worlds={[world('busy-1', 'ANALYZING')]} />);
  await wait();

  expect(refresh).toHaveBeenCalled();
});
