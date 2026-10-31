'use client';

import type { World } from '@/lib/api/client';
import { useAnalysisWatch } from '@/lib/use-analysis-watch';
import { WorldPhoto } from './world-photo';

/**
 * What the learner sees while the photo is being analysed (FR-021, US-020).
 *
 * Nothing here blocks them: the analysis runs on the server, so leaving the page
 * and coming back later shows the same result. The page says so, because a learner
 * who thinks they have to watch a spinner for 20 seconds will watch it.
 */
export function AnalysisProgress({ world }: { world: World }) {
  const { gaveUp } = useAnalysisWatch([world.id]);

  return (
    <>
      <WorldPhoto photoUrl={world.photoUrl} alt={`รูปภาพของ ${world.name}`} />

      <section
        className="glass-panel grid gap-2 rounded-3xl p-6"
        aria-live="polite"
      >
        {gaveUp ? (
          <>
            <h2 className="font-bold">ยังไม่ได้ผลลัพธ์</h2>
            <p className="text-sm text-muted-foreground">
              การวิเคราะห์นี้ใช้เวลานานผิดปกติ ลองรีเฟรชหน้านี้อีกครั้ง
              ถ้ายังไม่เสร็จ ระบบจะแจ้งว่าไม่สำเร็จเองและไม่ตัดโควตาของคุณ
            </p>
          </>
        ) : (
          <>
            <h2 className="font-bold">กำลังวิเคราะห์รูปภาพ…</h2>
            <p className="text-sm text-muted-foreground">
              ปกติใช้เวลาประมาณ 20 วินาที
              คุณออกจากหน้านี้ไปทำอย่างอื่นก่อนแล้วกลับมาดูผลทีหลังได้
            </p>
          </>
        )}
      </section>
    </>
  );
}
