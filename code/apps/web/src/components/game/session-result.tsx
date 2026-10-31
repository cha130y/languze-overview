import Link from 'next/link';
import { buttonVariants } from '@/components/ui/button';
import type { SessionSummary } from '@/lib/api/client';
import { masteryText } from '@/lib/mastery';
import { PlayActions } from './play-actions';

/**
 * The end of a session (FR-035, US-033): how many answers were right, the XP they
 * earned, and the words that moved — the only part of a session that lasts.
 *
 * Words that stayed where they were are not listed. A learner who answered ten
 * mastered words correctly achieved nothing new, and saying otherwise would make
 * the list mean nothing.
 */
export function SessionResult({
  summary,
  worldId,
}: {
  summary: SessionSummary;
  worldId: string | null | undefined;
}) {
  return (
    <section className="grid gap-5">
      <div className="glass-panel grid gap-2 rounded-3xl p-6">
        <h2 className="text-xl font-bold">จบเกมแล้ว</h2>
        <p className="text-sm">
          ตอบถูก {summary.correctCount} จาก {summary.answeredCount} ข้อ
        </p>
        <p className="text-sm font-semibold text-primary">
          ได้รับ {summary.xpEarned} XP
        </p>
      </div>

      <div className="glass-panel grid gap-3 rounded-3xl p-6">
        <h3 className="font-bold">ความคืบหน้าของคำศัพท์</h3>
        {summary.levelChanges.length > 0 ? (
          <ul className="grid gap-2">
            {summary.levelChanges.map((change) => (
              <li
                key={change.english}
                className="flex flex-wrap items-baseline justify-between gap-2 text-sm"
              >
                <span lang="en" className="font-semibold">
                  {change.english}
                </span>
                <span className="text-muted-foreground">
                  {masteryText(change.level)}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">
            รอบนี้ยังไม่มีคำไหนเปลี่ยนระดับ
            ลองเล่นอีกครั้งเพื่อเลื่อนระดับคำศัพท์
          </p>
        )}
      </div>

      {/* A review has no world, so it offers another review instead (FR-050). */}
      <div className="grid gap-3">
        <PlayActions
          kind={worldId ? 'GAME' : 'REVIEW'}
          worldId={worldId ?? undefined}
          openSession={null}
        />
        <Link
          href={worldId ? `/worlds/${worldId}` : '/review'}
          className={buttonVariants({ variant: 'outline', size: 'xl' })}
        >
          {worldId ? 'กลับไปที่โลกนี้' : 'กลับไปหน้าทบทวน'}
        </Link>
      </div>
    </section>
  );
}
