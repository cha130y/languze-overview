import type { Metadata } from 'next';
import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AiPaused } from '@/components/moderation/ai-paused';
import { AnalysisWatcher } from '@/components/worlds/analysis-watcher';
import { DailyAnalyses } from '@/components/worlds/daily-analyses';
import { WorldCard } from '@/components/worlds/world-card';
import { Button, buttonVariants } from '@/components/ui/button';
import { getAccount, getUsage, getWorlds } from '@/lib/api/server';

export const metadata: Metadata = { title: 'โลกของฉัน · LanguZe' };

/** Every world the learner has made (US-011), and what is left of today (US-080). */
export default async function WorldsPage() {
  const [account, worlds, usage] = await Promise.all([
    getAccount(),
    getWorlds(),
    getUsage(),
  ]);

  if (!account) redirect('/sign-in');
  if (!account.termsAccepted) redirect('/terms');

  // Only a count of zero stops a learner here; an unknown count is left to the API,
  // which refuses the upload itself if there is really nothing left (FR-020).
  const outOfAnalyses = usage?.analysesLeft === 0;
  const paused = account.aiAccess.reason === 'AI_SUSPENDED';

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-6 px-4 py-10">
      {/* Every page of the home menu leads back to it the same way (WCAG 3.2.3). */}
      <div>
        <Link
          href="/"
          className="rounded text-sm text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          ← หน้าแรก
        </Link>
        <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
          <div className="grid gap-1">
            <h1 className="text-2xl font-bold tracking-tight">โลกของฉัน</h1>
            {account.verifiedForAi && !paused ? (
              <DailyAnalyses usage={usage} />
            ) : null}
          </div>
          {account.verifiedForAi && !paused ? (
            outOfAnalyses ? (
              <Button variant="cta" size="xl" disabled>
                สร้างโลกใหม่
              </Button>
            ) : (
              <Link
                href="/worlds/new"
                className={buttonVariants({ variant: 'cta', size: 'xl' })}
              >
                สร้างโลกใหม่
              </Link>
            )
          ) : null}
        </div>
      </div>

      {/* A pause explains itself here, where the create button would have been. */}
      <AiPaused access={account.aiAccess} />

      {/* An unverified learner cannot analyse a photo yet, so say that first (FR-006). */}
      {!account.verifiedForAi && !paused ? (
        <section className="glass-panel grid gap-2 rounded-3xl p-6">
          <h2 className="font-bold">ยืนยันอีเมลก่อนสร้างโลก</h2>
          <p className="text-sm text-muted-foreground">
            การวิเคราะห์รูปภาพเป็นฟีเจอร์ AI จึงต้องยืนยันอีเมลก่อน
            เราส่งลิงก์ยืนยันไปให้แล้ว และขอใหม่ได้ที่หน้าแรก
          </p>
          <Link
            href="/"
            className={`${buttonVariants({ variant: 'outline', size: 'lg' })} mt-2 justify-self-start`}
          >
            ไปหน้าแรก
          </Link>
        </section>
      ) : worlds && worlds.length > 0 ? (
        <>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {worlds.map((world) => (
              <WorldCard key={world.id} world={world} />
            ))}
          </div>
          {/* Keeps the cards honest while an analysis is still running (P3). */}
          <AnalysisWatcher worlds={worlds} />
        </>
      ) : paused ? null : ( // The pause above has already said why there is nothing to do here.
        <section className="glass-panel grid gap-2 rounded-3xl p-6">
          <h2 className="font-bold">ยังไม่มีโลกของคุณ</h2>
          <p className="text-sm text-muted-foreground">
            ถ่ายรูปสิ่งรอบตัว เช่น ห้องครัวหรือโต๊ะทำงาน แล้ว LanguZe
            จะหาคำศัพท์ภาษาอังกฤษจากรูปนั้นให้คุณฝึก
          </p>
          <Link
            href="/worlds/new"
            className={`${buttonVariants({ variant: 'cta', size: 'xl' })} mt-2 justify-self-start`}
          >
            สร้างโลกแรกของคุณ
          </Link>
        </section>
      )}
    </main>
  );
}
