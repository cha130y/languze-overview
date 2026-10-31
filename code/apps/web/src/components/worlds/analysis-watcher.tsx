'use client';

import type { WorldSummary } from '@/lib/api/client';
import { useAnalysisWatch } from '@/lib/use-analysis-watch';

/**
 * Keeps a list of worlds up to date while any of them is being analysed (FR-021,
 * P3). It draws nothing: the cards next to it are server-rendered, and this only
 * asks the page to render them again once a result is in.
 */
export function AnalysisWatcher({ worlds }: { worlds: WorldSummary[] }) {
  useAnalysisWatch(
    worlds.filter((world) => world.status === 'ANALYZING').map(({ id }) => id),
  );
  return null;
}
