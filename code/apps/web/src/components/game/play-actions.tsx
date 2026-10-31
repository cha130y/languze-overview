'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button, buttonVariants } from '@/components/ui/button';
import { api, type PracticeSession } from '@/lib/api/client';
import { messageForError } from '@/lib/api/error-messages';

/** The same job in two voices: getting into a game, or into a review. */
const WORDS = {
  GAME: {
    start: 'เริ่มเล่นเกม',
    again: 'เริ่มเกมใหม่',
    starting: 'กำลังเริ่ม…',
    replaces:
      'การเริ่มเกมใหม่จะปิดเกมที่ค้างอยู่ และคำตอบที่ตอบไปแล้วจะยังถูกบันทึกไว้',
  },
  REVIEW: {
    start: 'เริ่มทบทวน',
    again: 'เริ่มทบทวนใหม่',
    starting: 'กำลังเริ่ม…',
    replaces:
      'การเริ่มทบทวนใหม่จะปิดรอบที่ค้างอยู่ และคำตอบที่ตอบไปแล้วจะยังถูกบันทึกไว้',
  },
} as const;

/**
 * Getting into a session (US-030, US-034, US-050).
 *
 * A session left unfinished is offered back before a new one, because starting a
 * new one closes it without a summary (FR-036) — that has to be the deliberate
 * choice, not the one that happens by pressing the obvious button.
 *
 * A game names its world; a review draws from all of them, so it names none. That
 * is the only difference, which is why both are this one component: two copies
 * would drift apart on the rule above, which is the part that matters.
 */
export function PlayActions({
  kind,
  worldId,
  openSession,
}: {
  kind: 'GAME' | 'REVIEW';
  worldId?: string;
  openSession: PracticeSession | null;
}) {
  const router = useRouter();
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const words = WORDS[kind];

  const start = async () => {
    setStarting(true);
    setError(null);
    try {
      const session =
        kind === 'REVIEW'
          ? await api.startReview()
          : await api.startGame(worldId ?? '');
      router.push(`/sessions/${session.id}`);
      router.refresh();
    } catch (caught) {
      setError(messageForError(caught));
      setStarting(false);
    }
  };

  return (
    <div className="grid gap-3">
      <div className="grid gap-3 sm:flex">
        {openSession ? (
          <Link
            href={`/sessions/${openSession.id}`}
            className={`${buttonVariants({ variant: 'cta', size: 'xl' })} sm:flex-1`}
          >
            เล่นต่อ (ข้อ {openSession.answeredCount + 1} จาก{' '}
            {openSession.questionCount})
          </Link>
        ) : null}

        <Button
          type="button"
          variant={openSession ? 'outline' : 'cta'}
          size="xl"
          className="sm:flex-1"
          disabled={starting}
          onClick={() => void start()}
        >
          {starting ? words.starting : openSession ? words.again : words.start}
        </Button>
      </div>

      {openSession ? (
        <p className="text-xs text-muted-foreground">{words.replaces}</p>
      ) : null}

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
