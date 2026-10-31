import Link from 'next/link';
import type { WorldSummary } from '@/lib/api/client';
import { WorldStatusBadge } from './world-status-badge';

/** One world in the list: photo, name, status and how far the learner has got. */
export function WorldCard({ world }: { world: WorldSummary }) {
  return (
    <Link
      href={`/worlds/${world.id}`}
      className="interactive-card glass-panel grid gap-3 rounded-3xl p-4 outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      {world.thumbnailUrl ? (
        // Signed links expire and point at another origin, so Next's image
        // optimisation would cache what it must not; the photo is served as it is.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={world.thumbnailUrl}
          alt=""
          className="aspect-4/3 w-full rounded-2xl object-cover"
        />
      ) : (
        <div className="grid aspect-4/3 w-full place-items-center rounded-2xl bg-muted text-sm text-muted-foreground">
          ไม่มีรูปภาพ
        </div>
      )}

      <div className="grid gap-2">
        <h2 className="font-semibold">{world.name}</h2>
        <WorldStatusBadge
          status={world.status}
          failureReason={world.failureReason}
        />
        <p className="text-xs text-muted-foreground">
          {world.wordCount} คำ · จำได้แล้ว {world.masteredCount} คำ
        </p>
      </div>
    </Link>
  );
}
