import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { api, type World } from '@/lib/api/client';
import { CreateWorldForm } from './create-world-form';

const push = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push, refresh: vi.fn() }),
}));

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { createWorld: vi.fn() },
}));

const world = { id: 'world-1', name: 'ห้องครัว' } as World;

const photo = (
  options: { type?: string; bytes?: number; name?: string } = {},
) =>
  new File(
    [new Uint8Array(options.bytes ?? 1024)],
    options.name ?? 'kitchen.jpg',
    { type: options.type ?? 'image/jpeg' },
  );

beforeEach(() => {
  push.mockReset();
  vi.mocked(api.createWorld).mockReset().mockResolvedValue(world);
  // jsdom has no object URLs, and the form makes one to preview the photo.
  URL.createObjectURL = vi.fn(() => 'blob:preview');
  URL.revokeObjectURL = vi.fn();
});

const fill = async (name: string, file: File | null) => {
  if (name) {
    await userEvent.type(screen.getByLabelText('ชื่อโลก'), name);
  }
  if (file) {
    /*
     * `applyAccept: false` because the input's `accept` list is a hint: a phone's
     * picker, a drag and drop, or a renamed file can all get past it. The point of
     * these tests is what the form does when something else arrives anyway.
     */
    await userEvent.upload(screen.getByLabelText('รูปภาพ'), file, {
      applyAccept: false,
    });
  }
  await userEvent.click(screen.getByRole('button', { name: 'สร้างโลกนี้' }));
};

test('uploads the photo and opens the new world', async () => {
  render(<CreateWorldForm />);
  const file = photo();

  await fill('ห้องครัว', file);

  expect(api.createWorld).toHaveBeenCalledWith('ห้องครัว', file);
  expect(push).toHaveBeenCalledWith('/worlds/world-1');
});

/** US-010 criterion 4: the name rule is explained, and nothing is uploaded. */
test('asks for a name before uploading anything', async () => {
  render(<CreateWorldForm />);

  await fill('', photo());

  expect(await screen.findByText('กรุณาตั้งชื่อโลกของคุณ')).toBeInTheDocument();
  expect(api.createWorld).not.toHaveBeenCalled();
});

test('asks for a photo', async () => {
  render(<CreateWorldForm />);

  await fill('ห้องครัว', null);

  expect(await screen.findByText('กรุณาเลือกรูปภาพ')).toBeInTheDocument();
  expect(api.createWorld).not.toHaveBeenCalled();
});

/*
 * US-010 criterion 3. The API decides the type from the file's content, but catching
 * the obvious cases here saves the learner a slow upload that would be refused.
 */
test('refuses a file that is not an accepted photo', async () => {
  render(<CreateWorldForm />);

  await fill('ห้องครัว', photo({ type: 'application/pdf', name: 'bill.pdf' }));

  expect(
    await screen.findByText('รองรับเฉพาะรูปภาพแบบ JPEG, PNG หรือ WebP'),
  ).toBeInTheDocument();
  expect(api.createWorld).not.toHaveBeenCalled();
});

test('refuses a photo over 10 MB', async () => {
  render(<CreateWorldForm />);

  await fill('ห้องครัว', photo({ bytes: 11 * 1024 * 1024 }));

  expect(
    await screen.findByText('รูปภาพต้องมีขนาดไม่เกิน 10 MB'),
  ).toBeInTheDocument();
  expect(api.createWorld).not.toHaveBeenCalled();
});

/** US-010 criterion 5: the limit is explained rather than shown as a failure. */
test('explains the limit when the learner already has twenty worlds', async () => {
  const { ApiError } = await import('@/lib/api/client');
  vi.mocked(api.createWorld).mockRejectedValue(
    new ApiError('WORLD_LIMIT_REACHED', 409, 'too many'),
  );
  render(<CreateWorldForm />);

  await fill('ห้องครัว', photo());

  expect(await screen.findByText(/ลบโลกที่ไม่ใช้แล้ว/)).toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
});

/* NFR-012: the form, and the form with its errors, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(<CreateWorldForm />);
  await expectNoAxeViolations(container);

  await userEvent.click(screen.getByRole('button', { name: 'สร้างโลกนี้' }));
  await expectNoAxeViolations(container);
});
