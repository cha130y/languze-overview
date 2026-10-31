import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { QuestionCard } from '@/components/game/question-card';
import { SessionResult } from '@/components/game/session-result';
import { buttonVariants } from '@/components/ui/button';
import { getAccount, getSession } from '@/lib/api/server';

export const metadata: Metadata = { title: 'ฝึกคำศัพท์ · LanguZe' };

/**
 * One sitting of practice: the question in front of the learner, or the result
 * when there are none left (FR-031, FR-035, US-030 to US-033).
 *
 * The page reads the session from the server every time, so a browser that reloads
 * it — which the LINE and Facebook apps do when the learner switches away and back
 * — lands on the next unanswered question rather than starting again (US-034).
 */
export default async function SessionPage({
  params,
}: {
  params: Promise<{ sessionId: string }>;
}) {
  const [{ sessionId }, account] = await Promise.all([params, getAccount()]);

  if (!account) redirect('/sign-in');
  if (!account.termsAccepted) redirect('/terms');

  // Another learner's session is answered as missing by the API (FR-008).
  const session = await getSession(sessionId);
  if (!session) notFound();

  const backHref = session.worldId ? `/worlds/${session.worldId}` : '/worlds';

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-4 py-10">
      <div>
        <Link
          href={backHref}
          className="rounded text-sm text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          ← ออกจากเกม
        </Link>
        <h1 className="mt-3 text-2xl font-bold tracking-tight">ฝึกคำศัพท์</h1>
      </div>

      {session.status === 'COMPLETED' && session.summary ? (
        <SessionResult summary={session.summary} worldId={session.worldId} />
      ) : session.status === 'IN_PROGRESS' && session.nextQuestion ? (
        /*
         * Keyed by the question: without it React keeps the card's own state
         * across a refresh, so the next question would arrive still wearing the
         * last one's feedback and with no way back to the answer box.
         */
        <QuestionCard
          key={session.nextQuestion.id}
          session={session}
          question={session.nextQuestion}
        />
      ) : (
        /*
         * A session that was abandoned, or one whose remaining words were all
         * removed. Either way there is nothing to answer and no summary to show
         * (FR-036), so the page says so and points back to the world.
         */
        <section className="glass-panel grid gap-2 rounded-3xl p-6">
          <h2 className="font-bold">เกมนี้จบไปแล้ว</h2>
          <p className="text-sm text-muted-foreground">
            เกมที่ค้างไว้เกิน 24 ชั่วโมง หรือถูกแทนที่ด้วยเกมใหม่
            จะปิดลงโดยไม่มีผลสรุป คำตอบที่ตอบไปแล้วยังถูกบันทึกไว้ทั้งหมด
          </p>
          <Link
            href={backHref}
            className={`${buttonVariants({ variant: 'cta', size: 'xl' })} mt-2 justify-self-start`}
          >
            กลับไปเริ่มเกมใหม่
          </Link>
        </section>
      )}
    </main>
  );
}
