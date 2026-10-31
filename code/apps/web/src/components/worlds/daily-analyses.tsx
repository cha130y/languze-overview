import type { Usage } from '@/lib/api/client';
import { resetsAtText } from '@/lib/daily-limit';

/**
 * How many photo analyses are left today (US-080, FR-080). Nothing is drawn when
 * the API could not say, because a wrong number here would either stop a learner
 * who has analyses left or promise ones they do not have.
 */
export function DailyAnalyses({ usage }: { usage: Usage | null }) {
  if (!usage) return null;

  return (
    <p className="text-sm text-muted-foreground">
      {usage.analysesLeft > 0
        ? `วันนี้วิเคราะห์รูปได้อีก ${usage.analysesLeft} จาก ${usage.analysesLimit} ครั้ง`
        : `วันนี้ใช้การวิเคราะห์ครบ ${usage.analysesLimit} ครั้งแล้ว เริ่มนับใหม่ ${resetsAtText(usage.resetsAt)} น. ตามเวลาไทย`}
    </p>
  );
}
