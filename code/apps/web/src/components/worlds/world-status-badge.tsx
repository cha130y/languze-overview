import { failureHeadline } from '@/lib/analysis-failures';
import type { World } from '@/lib/api/client';

/** The world's analysis status, in words a learner can act on (FR-013, FR-021). */
export function WorldStatusBadge({
  status,
  failureReason,
}: {
  status: World['status'];
  failureReason: World['failureReason'];
}) {
  if (status === 'ANALYZING') {
    return (
      <p className="glass-pill w-fit rounded-full px-3 py-1 text-xs font-semibold">
        กำลังวิเคราะห์…
      </p>
    );
  }

  if (status === 'FAILED') {
    return (
      <p className="w-fit rounded-full bg-destructive/15 px-3 py-1 text-xs font-semibold text-destructive">
        {failureHeadline(failureReason)}
      </p>
    );
  }

  return (
    <p className="w-fit rounded-full bg-primary/15 px-3 py-1 text-xs font-semibold text-primary">
      พร้อมเล่น
    </p>
  );
}
