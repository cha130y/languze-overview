import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { api, type World } from '@/lib/api/client';
import { WorldActions } from './world-actions';

const push = vi.fn();
const refresh = vi.fn();
vi.mock('next/navigation', () => ({ useRouter: () => ({ push, refresh }) }));

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { renameWorld: vi.fn(), deleteWorld: vi.fn() },
}));

const world = {
  id: 'world-1',
  name: 'ห้องครัว',
  status: 'READY',
  failureReason: null,
  photoUrl: 'https://photos.test/photo.jpg?X-Amz-Signature=abc',
  thumbnailUrl: 'https://photos.test/thumb.jpg?X-Amz-Signature=abc',
  wordCount: 0,
  masteredCount: 0,
  createdAt: '2026-09-24T10:00:00.000Z',
} as World;

beforeEach(() => {
  push.mockReset();
  refresh.mockReset();
  vi.mocked(api.renameWorld).mockReset().mockResolvedValue(world);
  vi.mocked(api.deleteWorld).mockReset().mockResolvedValue(undefined);
});

test('renames the world (US-013)', async () => {
  render(<WorldActions world={world} />);

  await userEvent.click(screen.getByRole('button', { name: 'เปลี่ยนชื่อ' }));
  const field = screen.getByLabelText('ชื่อโลก');
  await userEvent.clear(field);
  await userEvent.type(field, 'ห้องครัวของแม่');
  await userEvent.click(screen.getByRole('button', { name: 'บันทึกชื่อใหม่' }));

  expect(api.renameWorld).toHaveBeenCalledWith('world-1', 'ห้องครัวของแม่');
});

test('keeps the old name when the new one is empty (US-013)', async () => {
  render(<WorldActions world={world} />);

  await userEvent.click(screen.getByRole('button', { name: 'เปลี่ยนชื่อ' }));
  await userEvent.clear(screen.getByLabelText('ชื่อโลก'));
  await userEvent.click(screen.getByRole('button', { name: 'บันทึกชื่อใหม่' }));

  expect(await screen.findByText('กรุณาตั้งชื่อโลกของคุณ')).toBeInTheDocument();
  expect(api.renameWorld).not.toHaveBeenCalled();
});

/** US-014 criterion 5: deletion is permanent, so it is never one press away. */
test('asks before deleting, and deletes nothing on cancel', async () => {
  render(<WorldActions world={world} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบโลกนี้' }));
  expect(screen.getByText(/24 ชั่วโมง/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }));

  expect(api.deleteWorld).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'ลบโลกนี้' })).toBeInTheDocument();
});

test('deletes the world and returns to the list', async () => {
  render(<WorldActions world={world} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบโลกนี้' }));
  await userEvent.click(screen.getByRole('button', { name: 'ยืนยันลบโลกนี้' }));

  expect(api.deleteWorld).toHaveBeenCalledWith('world-1');
  expect(push).toHaveBeenCalledWith('/worlds');
});

test('keeps the world and explains when deleting fails', async () => {
  vi.mocked(api.deleteWorld).mockRejectedValue(new Error('network'));
  render(<WorldActions world={world} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบโลกนี้' }));
  await userEvent.click(screen.getByRole('button', { name: 'ยืนยันลบโลกนี้' }));

  expect(await screen.findByRole('alert')).toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
});

/*
 * WCAG 2.4.3: each step replaces the buttons that opened it, so focus moves into
 * the step and comes back to the button that opened it.
 */
test('keeps focus where the learner is through each step', async () => {
  render(<WorldActions world={world} />);

  await userEvent.click(screen.getByRole('button', { name: 'เปลี่ยนชื่อ' }));
  expect(screen.getByLabelText('ชื่อโลก')).toHaveFocus();
  await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }));
  expect(screen.getByRole('button', { name: 'เปลี่ยนชื่อ' })).toHaveFocus();

  await userEvent.click(screen.getByRole('button', { name: 'ลบโลกนี้' }));
  expect(screen.getByRole('button', { name: 'ยกเลิก' })).toHaveFocus();
  await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }));
  expect(screen.getByRole('button', { name: 'ลบโลกนี้' })).toHaveFocus();
});

/* NFR-012: each step: the buttons, renaming, and deleting, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(<WorldActions world={world} />);
  await expectNoAxeViolations(container);

  await userEvent.click(screen.getByRole('button', { name: 'เปลี่ยนชื่อ' }));
  await expectNoAxeViolations(container);
  await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }));

  await userEvent.click(screen.getByRole('button', { name: 'ลบโลกนี้' }));
  await expectNoAxeViolations(container);
});
