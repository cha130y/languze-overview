import type { Metadata } from 'next';
import Link from 'next/link';
import { redirect } from 'next/navigation';
import { AiPaused } from '@/components/moderation/ai-paused';
import { CreateWorldForm } from '@/components/worlds/create-world-form';
import { DailyAnalyses } from '@/components/worlds/daily-analyses';
import { buttonVariants } from '@/components/ui/button';
import { getAccount, getUsage } from '@/lib/api/server';
import { resetsAtText } from '@/lib/daily-limit';

export const metadata: Metadata = { title: 'สร้างโลกใหม่ · LanguZe' };

/** Naming a place and uploading its photo (US-010), within today's limit (US-080). */
export default async function NewWorldPage() {
  const [account, usage] = await Promise.all([getAccount(), getUsage()]);

  if (!account) redirect('/sign-in');
  if (!account.termsAccepted) redirect('/terms');
  // Analysis is an AI feature, so an unverified learner is sent back to the list,
  // which explains what to do about it (FR-006).
  if (!account.verifiedForAi) redirect('/worlds');

  /*
   * Uploading with nothing left would refuse the photo after it had been sent, on
   * a phone connection, so the form is not offered at all (US-080 criterion 2).
   * A count the API could not give is not treated as zero.
   */
  const exhausted = usage?.analysesLeft === 0 ? usage : null;
  // A paused learner is told here rather than sent away, because this is the page
  // they chose and the one the explanation answers (US-092 criterion 3).
  const paused = account.aiAccess.reason === 'AI_SUSPENDED';

  return (
    <main className="mx-auto flex w-full max-w-lg flex-1 flex-col gap-6 px-4 py-10">
      <div>
        <Link
          href="/worlds"
          className="rounded text-sm text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          ← โลกของฉัน
        </Link>
        <h1 className="mt-3 text-2xl font-bold tracking-tight">สร้างโลกใหม่</h1>
        {/* Not while paused: it would invite exactly what the panel refuses. */}
        {paused ? null : (
          <p className="mt-1 text-sm text-muted-foreground">
            ตั้งชื่อสถานที่ แล้วใส่รูปภาพหนึ่งรูป
          </p>
        )}
      </div>

      {paused ? (
        <AiPaused access={account.aiAccess} />
      ) : exhausted ? (
        <section className="glass-panel grid gap-2 rounded-3xl p-6">
          <h2 className="font-bold">วันนี้วิเคราะห์ครบแล้ว</h2>
          <p className="text-sm text-muted-foreground">
            คุณใช้การวิเคราะห์รูปภาพครบ {exhausted.analysesLimit}{' '}
            ครั้งของวันนี้แล้ว ระบบจะเริ่มนับใหม่{' '}
            {resetsAtText(exhausted.resetsAt)} น. ตามเวลาไทย
            ระหว่างนี้ฝึกคำศัพท์ในโลกที่มีอยู่ได้ตามปกติ
          </p>
          <Link
            href="/worlds"
            className={`${buttonVariants({ variant: 'outline', size: 'xl' })} mt-2 justify-self-start`}
          >
            กลับไปที่โลกของฉัน
          </Link>
        </section>
      ) : (
        // One shrinkable column, so the photo picker cannot widen the panel.
        <section className="glass-panel grid grid-cols-1 gap-4 rounded-3xl p-6">
          <DailyAnalyses usage={usage} />
          <CreateWorldForm />
        </section>
      )}
    </main>
  );
}
