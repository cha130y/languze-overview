import type { World } from '@/lib/api/client';
import { WorldPhoto } from './world-photo';

/**
 * What the world page shows while a game on it is unfinished (FR-014, S8).
 *
 * The words are the answers. Studying them before playing is the point of US-012;
 * reading them mid-game is not, and a mastery level earned that way would make
 * review, the tutor, and progress all describe a learner who does not exist.
 *
 * The photo stays, without its boxes: the numbers on them mean nothing without
 * the list beside them.
 */
export function WordsHidden({ world }: { world: World }) {
  return (
    <>
      <WorldPhoto photoUrl={world.photoUrl} alt={`รูปภาพของ ${world.name}`} />

      <section className="glass-panel grid gap-2 rounded-3xl p-6">
        <h2 className="font-bold">ซ่อนคำศัพท์ไว้ระหว่างเล่นเกม</h2>
        <p className="text-sm text-muted-foreground">
          โลกนี้มีเกมที่ยังเล่นไม่จบ เราจึงซ่อนรายการคำศัพท์ไว้ก่อน
          เพื่อให้เกมวัดว่าคุณจำได้จริง เมื่อเล่นเกมที่ค้างอยู่จบแล้ว
          รายการคำศัพท์ทั้ง {world.wordCount} คำ และการลบคำ จะกลับมาตามเดิม
        </p>
      </section>
    </>
  );
}
