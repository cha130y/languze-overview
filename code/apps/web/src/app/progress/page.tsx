import type { Metadata } from 'next';
import Link from 'next/link';
import { buttonVariants } from '@/components/ui/button';
import {
  getAccount,
  getProgress,
  getWorld,
  requireLearner,
} from '@/lib/api/server';
import { MASTERY_LEVELS, masteryText } from '@/lib/mastery';
import { BackLink } from '@/components/layout/back-link';
import {
  ProgressScene,
  type ProgressSceneWorld,
} from '@/components/scene/progress-scene';

export const metadata: Metadata = { title: 'ความคืบหน้า · LanguZe' };

/**
 * What the learner has to show for their practice (US-060, FR-061): the XP they
 * have earned, how many words stand at each level, and each world's share.
 *
 * XP only ever rises. Deleting a world takes its words away but never the points
 * they earned (V4), which is why the two are counted separately.
 */
export default async function ProgressPage() {
  const [account, progress] = await Promise.all([getAccount(), getProgress()]);

  requireLearner(account);

  if (!progress) {
    return (
      <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-4 py-10">
        <h1 className="text-2xl font-bold tracking-tight">ความคืบหน้า</h1>
        <p className="glass-panel rounded-3xl p-6 text-sm text-muted-foreground">
          ตอนนี้ยังดูความคืบหน้าไม่ได้ กรุณาลองใหม่อีกครั้ง
        </p>
      </main>
    );
  }

  const totalWords = MASTERY_LEVELS.reduce(
    (total, level) => total + progress.words[level],
    0,
  );

  const worldDetails = await Promise.all(
    progress.worlds.map((world) => getWorld(world.id)),
  );
  const sceneWorlds: ProgressSceneWorld[] = progress.worlds.map(
    (summary, index) => {
      const world = worldDetails[index];
      return {
        id: summary.id,
        name: world?.name ?? summary.name,
        wordCount: summary.wordCount,
        masteredCount: summary.masteredCount,
        words:
          world?.words.map(({ id, english, thaiMeaning, mastery }) => ({
            id,
            english,
            thaiMeaning,
            mastery,
          })) ?? [],
      };
    },
  );

  return (
    <main className="flex w-full flex-1 flex-col">
      <ProgressScene worlds={sceneWorlds} />
      <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8 md:px-8 md:py-10">
        <div>
          <BackLink href="/">หน้าแรก</BackLink>
          <h1 className="mt-3 text-2xl font-bold tracking-tight">
            ความคืบหน้า
          </h1>
        </div>

        <section className="glass-panel grid gap-1 rounded-3xl p-6">
          <h2 className="text-sm font-semibold text-muted-foreground">
            แต้มสะสมทั้งหมด
          </h2>
          <p className="text-4xl font-extrabold text-primary">
            {progress.totalXp} XP
          </p>
        </section>

        <section className="glass-panel grid gap-3 rounded-3xl p-6">
          <h2 className="font-bold">คำศัพท์ของฉัน ({totalWords} คำ)</h2>
          {totalWords > 0 ? (
            <dl className="grid gap-2">
              {MASTERY_LEVELS.map((level) => (
                <div
                  key={level}
                  className="flex items-baseline justify-between gap-2 text-sm"
                >
                  <dt>{masteryText(level)}</dt>
                  <dd className="font-semibold">{progress.words[level]} คำ</dd>
                </div>
              ))}
            </dl>
          ) : (
            <p className="text-sm text-muted-foreground">
              ยังไม่มีคำศัพท์ สร้างโลกจากรูปภาพของคุณเพื่อเริ่มเก็บคำศัพท์
            </p>
          )}
        </section>

        <section className="glass-panel grid gap-3 rounded-3xl p-6">
          <h2 className="font-bold">โลกของฉัน</h2>
          {progress.worlds.length > 0 ? (
            <ul className="grid gap-2">
              {progress.worlds.map((world) => (
                <li key={world.id}>
                  <Link
                    href={`/worlds/${world.id}`}
                    className="flex flex-wrap items-baseline justify-between gap-2 rounded text-sm underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
                  >
                    <span className="font-semibold">{world.name}</span>
                    <span className="text-muted-foreground">
                      {world.wordCount} คำ · จำได้แล้ว {world.masteredCount} คำ
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-muted-foreground">ยังไม่มีโลกของคุณ</p>
          )}
          <Link
            href="/worlds"
            className={`${buttonVariants({ variant: 'outline', size: 'xl' })} mt-2`}
          >
            ไปที่โลกของฉัน
          </Link>
        </section>
      </div>
    </main>
  );
}
