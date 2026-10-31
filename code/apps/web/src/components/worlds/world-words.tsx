'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { api, type World, type WorldWord } from '@/lib/api/client';
import { messageForError } from '@/lib/api/error-messages';
import { masteryText } from '@/lib/mastery';
import { WorldPhoto } from './world-photo';

/**
 * The words found in the photo, each with the box it came from (FR-014, US-022).
 *
 * The boxes carry a number that matches the list, so the pairing never depends on
 * colour or on being able to point at the picture (NFR-012). The boxes themselves
 * are hidden from assistive technology: they say nothing the list does not already
 * say, and a screen reader reading twelve empty rectangles helps nobody.
 */
export function WorldWords({ world }: { world: World }) {
  const router = useRouter();
  const [activeId, setActiveId] = useState<string | null>(null);
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  /*
   * Opening the question replaces the "ลบ" button, and cancelling brings it back,
   * so focus would fall to the top of the page both ways. The cancel button takes
   * it on opening, and the word's own button gets it back on cancel (WCAG 2.4.3).
   */
  const [returnFocusId, setReturnFocusId] = useState<string | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  /*
   * The last word cannot go: a world with nothing to practise is not a world, so
   * the learner deletes it instead (V5). The API refuses it too — this only keeps
   * the button from appearing when pressing it could never work.
   */
  const canRemove = world.words.length > 1;

  const remove = async (word: WorldWord) => {
    setRemovingId(word.id);
    setError(null);
    try {
      await api.removeWord(world.id, word.id);
      setConfirmingId(null);
      router.refresh();
    } catch (caught) {
      setError(messageForError(caught));
    } finally {
      setRemovingId(null);
    }
  };

  return (
    <>
      <WorldPhoto photoUrl={world.photoUrl} alt={`รูปภาพของ ${world.name}`}>
        <div aria-hidden className="pointer-events-none absolute inset-0">
          {world.words.map((word, index) => (
            <span
              key={word.id}
              className={`absolute rounded-xl border-2 transition-colors ${
                activeId === word.id
                  ? 'border-primary bg-primary/25'
                  : 'border-white/90'
              }`}
              style={{
                left: `${word.box.x * 100}%`,
                top: `${word.box.y * 100}%`,
                width: `${word.box.width * 100}%`,
                height: `${word.box.height * 100}%`,
              }}
            >
              <span className="absolute top-0 left-0 rounded-tl-[10px] rounded-br-lg bg-foreground/80 px-1.5 py-0.5 text-[11px] leading-none font-bold text-background">
                {index + 1}
              </span>
            </span>
          ))}
        </div>
      </WorldPhoto>

      <section className="grid gap-3">
        <h2 className="font-bold">คำศัพท์ {world.words.length} คำ</h2>

        {!canRemove ? (
          <p className="text-sm text-muted-foreground">
            โลกนี้เหลือคำศัพท์คำเดียว จึงลบคำออกไม่ได้อีก
            ถ้าไม่ต้องการโลกนี้แล้ว ให้ลบทั้งโลกด้านล่าง
          </p>
        ) : null}

        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}

        <ol className="grid gap-3">
          {world.words.map((word, index) => (
            <li
              key={word.id}
              className="glass-panel rounded-2xl p-4"
              onMouseEnter={() => setActiveId(word.id)}
              onMouseLeave={() => setActiveId(null)}
              onFocus={() => setActiveId(word.id)}
              onBlur={() => setActiveId(null)}
            >
              <div className="flex items-start gap-3">
                <span className="grid size-7 shrink-0 place-items-center rounded-full bg-foreground/80 text-xs font-bold text-background">
                  {index + 1}
                </span>

                <div className="grid flex-1 gap-1">
                  <p className="flex flex-wrap items-baseline gap-2">
                    <span lang="en" className="text-lg font-bold">
                      {word.english}
                    </span>
                    <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-semibold text-muted-foreground">
                      {word.cefrLevel}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {masteryText(word.mastery)}
                    </span>
                  </p>
                  <p className="text-sm">{word.thaiMeaning}</p>
                  <p lang="en" className="text-sm text-muted-foreground italic">
                    {word.exampleSentence}
                  </p>
                </div>

                {canRemove && confirmingId !== word.id ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={`ลบคำว่า ${word.english}`}
                    autoFocus={returnFocusId === word.id}
                    onClick={() => {
                      setConfirmingId(word.id);
                      setError(null);
                    }}
                  >
                    ลบ
                  </Button>
                ) : null}
              </div>

              {confirmingId === word.id ? (
                <div className="mt-3 grid gap-2 border-t border-border pt-3">
                  <p className="text-sm text-muted-foreground">
                    ลบ “<span lang="en">{word.english}</span>” ออกจากโลกนี้?
                    ถ้าคำนี้ไม่ได้อยู่ในโลกอื่นของคุณ
                    ความคืบหน้าของคำนี้จะหายไปด้วย
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      variant="destructive"
                      size="sm"
                      onClick={() => void remove(word)}
                      disabled={removingId === word.id}
                    >
                      {removingId === word.id ? 'กำลังลบ…' : 'ลบคำนี้'}
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      autoFocus
                      onClick={() => {
                        setConfirmingId(null);
                        setReturnFocusId(word.id);
                      }}
                      disabled={removingId === word.id}
                    >
                      ยกเลิก
                    </Button>
                  </div>
                </div>
              ) : null}
            </li>
          ))}
        </ol>
      </section>
    </>
  );
}
