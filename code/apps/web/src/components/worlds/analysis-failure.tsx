'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button, buttonVariants } from '@/components/ui/button';
import { countedAgainstLimit, failureAdvice } from '@/lib/analysis-failures';
import { api, type World } from '@/lib/api/client';
import { messageForError } from '@/lib/api/error-messages';
import { WorldPhoto } from './world-photo';

/**
 * A failed analysis and the way out of it (US-021).
 *
 * Retry is offered only while the photo is still there. A blocked photo is deleted
 * (FR-092), so that world can only be deleted — and the API refuses a retry anyway,
 * which is what actually decides; this is the same rule said on screen so the
 * learner is not offered a button that cannot work.
 */
export function AnalysisFailure({ world }: { world: World }) {
  const router = useRouter();
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canRetry = Boolean(world.photoUrl);

  const retry = async () => {
    setRetrying(true);
    setError(null);
    try {
      await api.retryAnalysis(world.id);
      // The world is ANALYZING again, which the page renders as the waiting panel.
      router.refresh();
    } catch (caught) {
      setError(messageForError(caught));
      setRetrying(false);
    }
  };

  return (
    <>
      <WorldPhoto photoUrl={world.photoUrl} alt={`รูปภาพของ ${world.name}`} />

      <section className="glass-panel grid gap-3 rounded-3xl p-6">
        <h2 className="font-bold">วิเคราะห์ไม่สำเร็จ</h2>
        <p className="text-sm text-muted-foreground">
          {failureAdvice(world.failureReason)}
        </p>
        {/* US-091: a general message that says where the rules are, never which one. */}
        {world.failureReason === 'BLOCKED' ? (
          <p className="text-sm text-muted-foreground">
            รูปภาพแบบไหนใช้ได้หรือไม่ได้ อ่านได้ที่{' '}
            <Link href="/terms-of-use" className="underline underline-offset-4">
              ข้อกำหนดการใช้งาน
            </Link>
          </p>
        ) : null}
        <p className="text-sm text-muted-foreground">
          {countedAgainstLimit(world.failureReason)
            ? 'การวิเคราะห์ครั้งนี้นับรวมในโควตาของวันนี้'
            : 'การวิเคราะห์ที่ไม่สำเร็จไม่ถูกนับในโควตาของวันนี้'}
        </p>

        <div className="mt-1 grid gap-3 sm:flex">
          {canRetry ? (
            <Button
              type="button"
              variant="cta"
              size="xl"
              className="sm:flex-1"
              onClick={() => void retry()}
              disabled={retrying}
            >
              {retrying ? 'กำลังเริ่มใหม่…' : 'วิเคราะห์รูปเดิมอีกครั้ง'}
            </Button>
          ) : null}
          <Link
            href="/worlds/new"
            className={`${buttonVariants({ variant: 'outline', size: 'xl' })} sm:flex-1`}
          >
            สร้างโลกใหม่ด้วยรูปอื่น
          </Link>
        </div>

        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
      </section>
    </>
  );
}
