'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { WorldPhoto } from '@/components/worlds/world-photo';
import {
  api,
  type AnswerResult,
  type PracticeSession,
  type Question,
} from '@/lib/api/client';
import { messageForError } from '@/lib/api/error-messages';
import { masteryText } from '@/lib/mastery';
import { useTypedBeforeHydration } from '@/lib/use-typed-before-hydration';

/** The longest answer the API accepts (V19); the input says so too. */
const MAX_ANSWER = 100;

/**
 * One question and what comes of answering it (FR-031, FR-033, US-031, US-032).
 *
 * The word is not here to be found: the page is given a photo and a box, and the
 * server decides whether the answer names the object (S5). That is also why the
 * feedback below can only appear once an answer has been sent.
 *
 * The form is a plain one rather than React Hook Form: there is a single field
 * whose only rule is a length the input already enforces, and the answer that
 * matters is judged on the server. It takes focus on sight, because typing a word
 * is the only thing this page is for.
 */
export function QuestionCard({
  session,
  question,
}: {
  session: PracticeSession;
  question: Question;
}) {
  const router = useRouter();
  const [answer, setAnswer] = useState('');
  const [result, setResult] = useState<AnswerResult | null>(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // The box takes focus on sight, so on a page loaded straight here it is typed in early.
  const answerBox = useRef<HTMLInputElement>(null);
  useTypedBeforeHydration(answerBox, setAnswer);

  const send = async (body: { answer?: string; dontKnow?: boolean }) => {
    setSending(true);
    setError(null);
    try {
      setResult(await api.answerQuestion(session.id, question.id, body));
    } catch (caught) {
      setError(messageForError(caught));
    } finally {
      setSending(false);
    }
  };

  return (
    <section className="grid gap-5">
      <p className="text-sm text-muted-foreground">
        ข้อ {question.position} จาก {session.questionCount}
      </p>

      <WorldPhoto
        photoUrl={question.photoUrl}
        alt="รูปภาพของคุณ โดยมีกรอบล้อมรอบสิ่งของที่ต้องตอบ"
      >
        {/*
         * One box, drawn as a shape rather than a colour: a white outline inside a
         * dark one reads on any photo, and on a screen where colour is lost
         * (NFR-012, US-030 criterion 4).
         */}
        <span
          aria-hidden
          className="pointer-events-none absolute rounded-xl border-4 border-white ring-3 ring-black/70 ring-inset"
          style={{
            left: `${question.box.x * 100}%`,
            top: `${question.box.y * 100}%`,
            width: `${question.box.width * 100}%`,
            height: `${question.box.height * 100}%`,
          }}
        />
      </WorldPhoto>

      {result ? (
        <Feedback
          result={result}
          onNext={() => {
            // The server holds where the session has got to, so the page asks again.
            router.refresh();
          }}
        />
      ) : (
        <form
          className="grid gap-3"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            void send({ answer });
          }}
        >
          <label htmlFor="answer" className="font-semibold">
            สิ่งของในกรอบนี้ ภาษาอังกฤษเรียกว่าอะไร?
          </label>
          <input
            id="answer"
            ref={answerBox}
            name="answer"
            // The answer is English: screen readers and keyboards should treat it so (WCAG 3.1.2).
            lang="en"
            autoFocus
            autoComplete="off"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            maxLength={MAX_ANSWER}
            value={answer}
            onChange={(event) => setAnswer(event.target.value)}
            className="rounded-xl border border-input bg-input/30 p-3 text-lg outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
          />
          <div className="grid gap-3 sm:flex">
            <Button
              type="submit"
              variant="cta"
              size="xl"
              className="sm:flex-1"
              disabled={sending || answer.trim() === ''}
            >
              {sending ? 'กำลังตรวจ…' : 'ตอบ'}
            </Button>
            {/* US-032: saying so is a real choice, not a punishment for guessing. */}
            <Button
              type="button"
              variant="outline"
              size="xl"
              className="sm:flex-1"
              disabled={sending}
              onClick={() => void send({ dontKnow: true })}
            >
              ไม่ทราบ
            </Button>
          </div>
        </form>
      )}

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </section>
  );
}

/**
 * What the answer was worth (FR-033). Right or wrong, the word is taught here:
 * an answer the learner never sees the truth of teaches them nothing.
 *
 * The learner's own answer is shown back when it differs from the word: a wrong
 * one, to compare with the word, and a right one that was an accepted variant,
 * because `tree` marked right beside the word `plant` otherwise looks like a mistake.
 */
function Feedback({
  result,
  onNext,
}: {
  result: AnswerResult;
  onNext: () => void;
}) {
  // Never an empty string: the API refuses one (UC-031 1b).
  const typed =
    result.answer && (!result.correct || result.matchedVariant)
      ? result.answer
      : null;
  const wordLabel = result.correct
    ? result.matchedVariant
      ? 'คำหลักในโลกของคุณ'
      : null
    : result.dontKnow
      ? 'คำนี้คือ'
      : 'คำที่ถูกคือ';

  return (
    <div
      className={`glass-panel grid gap-3 rounded-3xl p-6 ${
        result.correct ? 'ring-2 ring-primary/40' : ''
      }`}
    >
      {/*
       * Only the verdict is announced, and it is all Thai. When the whole panel was
       * the live region, VoiceOver read it out as one string and spoke its English
       * with Thai sounds, `lang` or not. Read an element at a time, as the learner
       * moves back from "ข้อต่อไป", the English keeps its English voice (A6).
       *
       * Not colour alone: the mark and the words both say which it was.
       */}
      <p role="status" className="flex items-center gap-2 text-lg font-bold">
        <span aria-hidden>{result.correct ? '✓' : '✕'}</span>
        {result.correct
          ? `ถูกต้อง +${result.xpAwarded} XP`
          : result.dontKnow
            ? 'ไม่เป็นไร'
            : 'ยังไม่ถูก'}
      </p>

      {typed !== null ? (
        <p>
          คุณตอบ:{' '}
          <span lang="en" className="font-semibold">
            {typed}
          </span>
          {result.matchedVariant ? ' (ใช้ได้เช่นกัน)' : null}
        </p>
      ) : null}

      <div className="grid gap-1">
        {wordLabel ? (
          <p className="text-sm text-muted-foreground">{wordLabel}</p>
        ) : null}
        {/* Read in English, not with Thai sounds (WCAG 3.1.2). */}
        <p lang="en" className="text-xl font-bold">
          {result.word.english}
        </p>
        <p className="text-sm">{result.word.thaiMeaning}</p>
        <p lang="en" className="text-sm text-muted-foreground italic">
          {result.word.exampleSentence}
        </p>
      </div>

      {result.mastery.before !== result.mastery.after ? (
        <p className="text-sm text-muted-foreground">
          ระดับความจำของคำนี้: {masteryText(result.mastery.after)}
        </p>
      ) : null}

      {/*
       * The form that had focus is gone, so focus would fall back to the top of the
       * page. It moves here instead: a keyboard learner reads the feedback and goes
       * on with Enter (WCAG 2.4.3).
       */}
      <Button
        type="button"
        variant="cta"
        size="xl"
        className="mt-1"
        autoFocus
        onClick={onNext}
      >
        {result.sessionCompleted ? 'ดูผลสรุป' : 'ข้อต่อไป'}
      </Button>
    </div>
  );
}
