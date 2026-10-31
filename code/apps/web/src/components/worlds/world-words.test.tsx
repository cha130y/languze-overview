import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import { ApiError, api, type World, type WorldWord } from '@/lib/api/client';
import { WorldWords } from './world-words';

const { refresh } = vi.hoisted(() => ({ refresh: vi.fn() }));
vi.mock('next/navigation', () => {
  const router = { refresh, push: vi.fn() };
  return { useRouter: () => router };
});

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { removeWord: vi.fn() },
}));

const sofa: WorldWord = {
  id: 'word-1',
  english: 'sofa',
  thaiMeaning: 'โซฟา',
  exampleSentence: 'We sit on the sofa.',
  cefrLevel: 'A1',
  box: { x: 0.2, y: 0.1, width: 0.4, height: 0.25 },
  mastery: 'NEW',
};

const lamp: WorldWord = {
  ...sofa,
  id: 'word-2',
  english: 'lamp',
  thaiMeaning: 'โคมไฟ',
  cefrLevel: 'A2',
  exampleSentence: 'The lamp is on the table.',
  box: { x: 0.6, y: 0.5, width: 0.2, height: 0.2 },
};

const worldOf = (words: WorldWord[]) =>
  ({
    id: 'world-1',
    name: 'ห้องครัว',
    status: 'READY',
    failureReason: null,
    photoUrl: 'https://photos.test/photo.jpg?X-Amz-Signature=abc',
    thumbnailUrl: null,
    wordCount: words.length,
    masteredCount: 0,
    createdAt: '2026-09-24T10:00:00.000Z',
    words,
  }) as World;

const boxesOf = (container: HTMLElement) => [
  ...container.querySelectorAll<HTMLElement>('[aria-hidden="true"] > span'),
];

beforeEach(() => {
  refresh.mockReset();
  vi.mocked(api.removeWord).mockReset().mockResolvedValue(undefined);
});

test('shows every word with its meaning and example (FR-014)', () => {
  render(
    <WorldWords world={worldOf([sofa, { ...lamp, mastery: 'FAMILIAR' }])} />,
  );

  expect(screen.getByText('คำศัพท์ 2 คำ')).toBeInTheDocument();
  expect(screen.getByText('sofa')).toBeInTheDocument();
  expect(screen.getByText('โซฟา')).toBeInTheDocument();
  expect(screen.getByText('We sit on the sofa.')).toBeInTheDocument();
  expect(screen.getByText('A1')).toBeInTheDocument();
  // US-012 criterion 1: each word with the learner's own mastery of it.
  expect(screen.getByText('ยังไม่ได้ฝึก')).toBeInTheDocument();
  expect(screen.getByText('เริ่มคุ้น')).toBeInTheDocument();
});

/* The box the AI returned, as a share of the picture: 0.2 across is 20% across. */
test('puts each highlight box where the AI found the object (AIR-003)', () => {
  const { container } = render(<WorldWords world={worldOf([sofa, lamp])} />);

  const [first, second] = boxesOf(container);
  expect(first.style.left).toBe('20%');
  expect(first.style.top).toBe('10%');
  expect(first.style.width).toBe('40%');
  expect(first.style.height).toBe('25%');
  expect(second.style.left).toBe('60%');
});

/**
 * NFR-012: which box belongs to which word cannot depend on colour, so the box
 * carries the same number as the row.
 */
test('numbers the boxes to match the list', () => {
  const { container } = render(<WorldWords world={worldOf([sofa, lamp])} />);

  expect(boxesOf(container).map((box) => box.textContent)).toEqual(['1', '2']);
  expect(within(screen.getByRole('list')).getAllByText('2')).not.toHaveLength(
    0,
  );
});

test('points out the word being pointed at', async () => {
  const { container } = render(<WorldWords world={worldOf([sofa, lamp])} />);
  const [first] = boxesOf(container);
  expect(first.className).not.toContain('border-primary');

  await userEvent.hover(screen.getByText('sofa'));

  expect(boxesOf(container)[0].className).toContain('border-primary');
});

test('asks before removing a word, and removes nothing on cancel (US-022)', async () => {
  render(<WorldWords world={worldOf([sofa, lamp])} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบคำว่า sofa' }));
  // The word is marked as English inside the Thai question, so it spans elements.
  expect(
    screen.getByText(
      (_, element) =>
        element?.tagName === 'P' &&
        (element.textContent ?? '').startsWith('ลบ “sofa” ออกจากโลกนี้?'),
    ),
  ).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }));

  expect(api.removeWord).not.toHaveBeenCalled();
});

/* WCAG 2.4.3: the button pressed disappears both ways, so focus has to be moved. */
test('keeps focus where the learner is when the question opens and closes', async () => {
  render(<WorldWords world={worldOf([sofa, lamp])} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบคำว่า lamp' }));
  expect(screen.getByRole('button', { name: 'ยกเลิก' })).toHaveFocus();

  await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }));
  expect(screen.getByRole('button', { name: 'ลบคำว่า lamp' })).toHaveFocus();
});

/* WCAG 3.1.2: English read with Thai sounds is no use to a learner. */
test('marks the English word and its example as English', () => {
  render(<WorldWords world={worldOf([sofa, lamp])} />);

  expect(screen.getByText('sofa')).toHaveAttribute('lang', 'en');
  expect(screen.getByText(sofa.exampleSentence)).toHaveAttribute('lang', 'en');
});

test('removes the word the learner chose (US-022)', async () => {
  render(<WorldWords world={worldOf([sofa, lamp])} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบคำว่า lamp' }));
  await userEvent.click(screen.getByRole('button', { name: 'ลบคำนี้' }));

  expect(api.removeWord).toHaveBeenCalledWith('world-1', 'word-2');
  expect(refresh).toHaveBeenCalled();
});

/** V5: the last word stays, and the learner is told what to do instead. */
test('offers no way to remove the only word left', () => {
  render(<WorldWords world={worldOf([sofa])} />);

  expect(screen.queryByRole('button', { name: 'ลบคำว่า sofa' })).toBeNull();
  expect(screen.getByText(/ให้ลบทั้งโลกด้านล่าง/)).toBeInTheDocument();
});

/* The API decides, so its refusal is shown even when the page thought it was fine. */
test('explains a refusal from the API', async () => {
  vi.mocked(api.removeWord).mockRejectedValue(
    new ApiError('LAST_WORD', 409, 'A world keeps at least one word.'),
  );
  render(<WorldWords world={worldOf([sofa, lamp])} />);

  await userEvent.click(screen.getByRole('button', { name: 'ลบคำว่า sofa' }));
  await userEvent.click(screen.getByRole('button', { name: 'ลบคำนี้' }));

  expect(await screen.findByRole('alert')).toHaveTextContent(
    /อย่างน้อยหนึ่งคำ/,
  );
  expect(refresh).not.toHaveBeenCalled();
});

/* NFR-012: the list, and the question before removing a word, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(<WorldWords world={worldOf([sofa, lamp])} />);
  await expectNoAxeViolations(container);

  await userEvent.click(screen.getByRole('button', { name: 'ลบคำว่า sofa' }));
  await expectNoAxeViolations(container);
});
