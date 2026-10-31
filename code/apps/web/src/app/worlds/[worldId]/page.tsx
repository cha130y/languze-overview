import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound, redirect } from 'next/navigation';
import { PlayActions } from '@/components/game/play-actions';
import { AnalysisFailure } from '@/components/worlds/analysis-failure';
import { AnalysisProgress } from '@/components/worlds/analysis-progress';
import { WorldActions } from '@/components/worlds/world-actions';
import { WorldStatusBadge } from '@/components/worlds/world-status-badge';
import { WordsHidden } from '@/components/worlds/words-hidden';
import { WorldWords } from '@/components/worlds/world-words';
import { getAccount, getCurrentGame, getWorld } from '@/lib/api/server';

export const metadata: Metadata = { title: 'โลกของฉัน · LanguZe' };

/**
 * One world: its photo, and whichever of the three things is true of it — being
 * analysed, failed, or a list of words to practise (FR-014, FR-021, US-012).
 */
export default async function WorldPage({
  params,
}: {
  params: Promise<{ worldId: string }>;
}) {
  const [{ worldId }, account] = await Promise.all([params, getAccount()]);

  if (!account) redirect('/sign-in');
  if (!account.termsAccepted) redirect('/terms');

  // Another learner's world is answered as missing by the API, and shown as missing
  // here, so this page never confirms that it exists (FR-008).
  const world = await getWorld(worldId);
  if (!world) notFound();

  /*
   * Only a world that can be played is asked about: a world still being analysed
   * has no game to continue, and asking would be one request for nothing.
   */
  const openGame =
    world.status === 'READY' ? await getCurrentGame(world.id) : null;

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-4 py-10">
      <div>
        <Link
          href="/worlds"
          className="rounded text-sm text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          ← โลกของฉัน
        </Link>
        <h1 className="mt-3 text-2xl font-bold tracking-tight">{world.name}</h1>
        <div className="mt-2">
          <WorldStatusBadge
            status={world.status}
            failureReason={world.failureReason}
          />
        </div>
      </div>

      {world.status === 'ANALYZING' ? (
        <AnalysisProgress world={world} />
      ) : world.status === 'FAILED' ? (
        <AnalysisFailure world={world} />
      ) : (
        <>
          <section className="glass-panel grid gap-4 rounded-3xl p-6">
            <h2 className="font-bold">ฝึกคำศัพท์ของโลกนี้</h2>
            <PlayActions
              kind="GAME"
              worldId={world.id}
              openSession={openGame}
            />
          </section>
          {/* The words are the answers, so they wait until the game is over (S8). */}
          {openGame ? (
            <WordsHidden world={world} />
          ) : (
            <WorldWords world={world} />
          )}
        </>
      )}

      <section className="glass-panel grid gap-4 rounded-3xl p-6">
        <h2 className="font-bold">จัดการโลกนี้</h2>
        <WorldActions world={world} />
      </section>
    </main>
  );
}
