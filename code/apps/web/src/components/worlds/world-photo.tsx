/**
 * A world's photo, with room for highlight boxes on top of it (FR-014, FR-031).
 *
 * The image keeps its own proportions and is never cropped, because the boxes are
 * placed as percentages of it: a crop would move every box off its object. Nothing
 * is missing when the photo is gone — a blocked photo is deleted (FR-092) — so that
 * case is a panel saying so rather than a broken picture.
 */
export function WorldPhoto({
  photoUrl,
  alt,
  children,
}: {
  photoUrl: string | null | undefined;
  alt: string;
  children?: React.ReactNode;
}) {
  if (!photoUrl) {
    return (
      <div className="glass-panel grid min-h-40 place-items-center rounded-3xl p-6 text-sm text-muted-foreground">
        ไม่มีรูปภาพของโลกนี้แล้ว
      </div>
    );
  }

  return (
    <div className="relative overflow-hidden rounded-3xl">
      {/*
       * The link is signed and expires, so Next's image optimisation is not used:
       * it would cache a photo that only this learner may see (P4, NFR-008).
       */}
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={photoUrl} alt={alt} className="block h-auto w-full" />
      {children}
    </div>
  );
}
