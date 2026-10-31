import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, test, vi } from 'vitest';
import { expectNoAxeViolations } from '@/test/axe';
import {
  ApiError,
  api,
  type AnswerResult,
  type PracticeSession,
  type Question,
} from '@/lib/api/client';
import { QuestionCard } from './question-card';

const { refresh } = vi.hoisted(() => ({ refresh: vi.fn() }));
vi.mock('next/navigation', () => {
  const router = { refresh, push: vi.fn() };
  return { useRouter: () => router };
});

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { answerQuestion: vi.fn() },
}));

const session = {
  id: 'session-1',
  kind: 'GAME',
  status: 'IN_PROGRESS',
  worldId: 'world-1',
  answeredCount: 2,
  questionCount: 6,
  startedAt: '2026-09-25T04:00:00.000Z',
  summary: null,
} as PracticeSession;

const question: Question = {
  id: 'question-3',
  position: 3,
  photoUrl: 'https://photos.test/photo.jpg?X-Amz-Signature=abc',
  box: { x: 0.2, y: 0.1, width: 0.4, height: 0.25 },
};

const result = (overrides: Partial<AnswerResult> = {}): AnswerResult => ({
  correct: true,
  dontKnow: false,
  alreadyAnswered: false,
  answer: 'sofa',
  matchedVariant: false,
  word: {
    english: 'sofa',
    thaiMeaning: 'โซฟา',
    exampleSentence: 'We sit on the sofa.',
  },
  xpAwarded: 10,
  mastery: { before: null, after: 'LEARNING' },
  sessionCompleted: false,
  summary: null,
  ...overrides,
});

beforeEach(() => {
  refresh.mockReset();
  vi.mocked(api.answerQuestion).mockReset().mockResolvedValue(result());
});

test('shows the photo and the box, and asks for the word (FR-031)', () => {
  const { container } = render(
    <QuestionCard session={session} question={question} />,
  );

  expect(screen.getByText('ข้อ 3 จาก 6')).toBeInTheDocument();
  expect(screen.getByLabelText(/ภาษาอังกฤษเรียกว่าอะไร/)).toHaveFocus();
  const box = container.querySelector<HTMLElement>('[aria-hidden="true"]');
  expect(box?.style.left).toBe('20%');
  expect(box?.style.width).toBe('40%');
});

/*
 * S5: the word is checked on the server, so it can never be in the page before
 * the learner answers. This is the test that would notice if it ever were.
 */
test('has no sight of the answer before one is given', () => {
  const { container } = render(
    <QuestionCard session={session} question={question} />,
  );

  expect(container.innerHTML).not.toContain('sofa');
  expect(container.innerHTML).not.toContain('โซฟา');
});

test('sends the typed answer and teaches the word (US-031)', async () => {
  render(<QuestionCard session={session} question={question} />);

  await userEvent.type(screen.getByLabelText(/ภาษาอังกฤษ/), 'sofa');
  await userEvent.click(screen.getByRole('button', { name: 'ตอบ' }));

  expect(api.answerQuestion).toHaveBeenCalledWith('session-1', 'question-3', {
    answer: 'sofa',
  });
  expect(await screen.findByText(/ถูกต้อง \+10 XP/)).toBeInTheDocument();
  expect(screen.getByText('sofa')).toBeInTheDocument();
  expect(screen.getByText('โซฟา')).toBeInTheDocument();
  expect(screen.getByText('We sit on the sofa.')).toBeInTheDocument();
  // The answer was the word itself, so it is not repeated back.
  expect(screen.queryByText(/คุณตอบ/)).toBeNull();
});

/* US-031 criterion 2: a wrong answer still teaches the word. */
test('teaches the word after a wrong answer too, beside what was typed', async () => {
  vi.mocked(api.answerQuestion).mockResolvedValue(
    result({
      correct: false,
      answer: 'zzz',
      xpAwarded: 0,
      mastery: { before: null, after: 'LEARNING' },
    }),
  );
  render(<QuestionCard session={session} question={question} />);

  await userEvent.type(screen.getByLabelText(/ภาษาอังกฤษ/), 'zzz');
  await userEvent.click(screen.getByRole('button', { name: 'ตอบ' }));

  expect(await screen.findByText(/ยังไม่ถูก/)).toBeInTheDocument();
  expect(screen.getByText(/คุณตอบ/)).toHaveTextContent('คุณตอบ: zzz');
  expect(screen.getByText('คำที่ถูกคือ')).toBeInTheDocument();
  expect(screen.getByText('sofa')).toBeInTheDocument();
  expect(screen.queryByText(/XP/)).toBeNull();
});

/*
 * FR-033: a variant is right, but the word shown is another one. Without its
 * own answer beside it, the learner reads the feedback as a correction.
 */
test('says an accepted variant counted, and which word was saved', async () => {
  vi.mocked(api.answerQuestion).mockResolvedValue(
    result({ answer: 'couch', matchedVariant: true }),
  );
  const { container } = render(
    <QuestionCard session={session} question={question} />,
  );

  await userEvent.type(screen.getByLabelText(/ภาษาอังกฤษ/), 'couch');
  await userEvent.click(screen.getByRole('button', { name: 'ตอบ' }));

  expect(await screen.findByText(/ถูกต้อง \+10 XP/)).toBeInTheDocument();
  expect(screen.getByText(/คุณตอบ/)).toHaveTextContent(
    'คุณตอบ: couch (ใช้ได้เช่นกัน)',
  );
  expect(screen.getByText('couch')).toHaveAttribute('lang', 'en');
  expect(screen.getByText('คำหลักในโลกของคุณ')).toBeInTheDocument();
  expect(screen.getByText('sofa')).toBeInTheDocument();
  await expectNoAxeViolations(container);
});

/* US-032: saying so is a choice of its own, not a guess with an empty box. */
test('offers "I don’t know" and sends it as such', async () => {
  vi.mocked(api.answerQuestion).mockResolvedValue(
    result({ correct: false, dontKnow: true, answer: null, xpAwarded: 0 }),
  );
  render(<QuestionCard session={session} question={question} />);

  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));

  expect(api.answerQuestion).toHaveBeenCalledWith('session-1', 'question-3', {
    dontKnow: true,
  });
  expect(await screen.findByText(/ไม่เป็นไร/)).toBeInTheDocument();
  expect(screen.getByText('คำนี้คือ')).toBeInTheDocument();
  expect(screen.queryByText(/คุณตอบ/)).toBeNull();
});

/*
 * A6: VoiceOver reads a live region out as one string and speaks its English
 * with Thai sounds, so only the Thai verdict is announced. The English is read
 * element by element, in its own voice, once the learner moves to it.
 */
test('announces only the verdict, with no English in it', async () => {
  vi.mocked(api.answerQuestion).mockResolvedValue(
    result({ answer: 'couch', matchedVariant: true }),
  );
  render(<QuestionCard session={session} question={question} />);

  await userEvent.type(screen.getByLabelText(/ภาษาอังกฤษ/), 'couch');
  await userEvent.click(screen.getByRole('button', { name: 'ตอบ' }));

  const announced = await screen.findByRole('status');
  expect(announced).toHaveTextContent('ถูกต้อง +10 XP');
  for (const english of ['couch', 'sofa', 'We sit on the sofa.']) {
    expect(announced).not.toHaveTextContent(english);
    expect(screen.getByText(english)).toBeInTheDocument();
  }
});

test('will not send an empty answer', async () => {
  render(<QuestionCard session={session} question={question} />);

  expect(screen.getByRole('button', { name: 'ตอบ' })).toBeDisabled();
  await userEvent.type(screen.getByLabelText(/ภาษาอังกฤษ/), '   ');
  expect(screen.getByRole('button', { name: 'ตอบ' })).toBeDisabled();
  expect(api.answerQuestion).not.toHaveBeenCalled();
});

test('asks the server for the next question (FR-036)', async () => {
  render(<QuestionCard session={session} question={question} />);

  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));
  await userEvent.click(
    await screen.findByRole('button', { name: 'ข้อต่อไป' }),
  );

  expect(refresh).toHaveBeenCalled();
});

test('offers the summary when that was the last question (US-033)', async () => {
  vi.mocked(api.answerQuestion).mockResolvedValue(
    result({ sessionCompleted: true }),
  );
  render(<QuestionCard session={session} question={question} />);

  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));

  expect(
    await screen.findByRole('button', { name: 'ดูผลสรุป' }),
  ).toBeInTheDocument();
});

test('explains a refusal and keeps the question on screen', async () => {
  vi.mocked(api.answerQuestion).mockRejectedValue(
    new ApiError('SESSION_CLOSED', 409, 'closed'),
  );
  render(<QuestionCard session={session} question={question} />);

  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));

  expect(await screen.findByRole('alert')).toHaveTextContent(/เกมนี้ปิดไปแล้ว/);
  expect(screen.getByLabelText(/ภาษาอังกฤษ/)).toBeInTheDocument();
});

/*
 * WCAG 2.4.3: the form that held focus is replaced by the feedback, so focus
 * moves to the next step instead of falling to the top of the page.
 */
test('puts focus on the next step once the feedback shows', async () => {
  render(<QuestionCard session={session} question={question} />);

  await userEvent.type(screen.getByLabelText(/ภาษาอังกฤษเรียกว่าอะไร/), 'sofa');
  await userEvent.click(screen.getByRole('button', { name: 'ตอบ' }));

  expect(await screen.findByRole('button', { name: 'ข้อต่อไป' })).toHaveFocus();
});

/* WCAG 3.1.2: English read with Thai sounds is no use to a learner. */
test('marks the answer and the word taught as English', async () => {
  render(<QuestionCard session={session} question={question} />);

  expect(screen.getByLabelText(/ภาษาอังกฤษเรียกว่าอะไร/)).toHaveAttribute(
    'lang',
    'en',
  );
  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));

  expect(await screen.findByText('sofa')).toHaveAttribute('lang', 'en');
  expect(screen.getByText('We sit on the sofa.')).toHaveAttribute('lang', 'en');
});

/* NFR-012: the question, and the feedback after it, checked by axe-core. */
test('has no accessibility violations (NFR-012)', async () => {
  const { container } = render(
    <QuestionCard session={session} question={question} />,
  );
  await expectNoAxeViolations(container);

  await userEvent.click(screen.getByRole('button', { name: 'ไม่ทราบ' }));
  await screen.findByRole('button', { name: 'ข้อต่อไป' });
  await expectNoAxeViolations(container);
});
