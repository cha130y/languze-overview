# Design system

Status: draft · Release 1.0

The visual language of the LanguZe web app: a dark, glass-surfaced interface built on shadcn/ui primitives and Tailwind tokens. This document explains what the tokens mean and when to use each one, so every screen added after this one looks like it belongs.

## 1. Where it lives

| Thing                         | File                                                                    |
| ----------------------------- | ----------------------------------------------------------------------- |
| Colour, radius, motion tokens | `apps/web/src/app/globals.css`                                          |
| Surface and CTA classes       | `apps/web/src/app/globals.css`, `@layer components`                     |
| Fonts                         | `apps/web/src/app/layout.tsx`                                           |
| Components                    | `apps/web/src/components/ui/` (shadcn), `apps/web/src/components/auth/` |

Change the tokens, not the components. A screen that needs a colour the tokens do not have needs a new token, discussed here first.

## 2. Dark only

The interface has one theme. The palette sits on `:root` with no light counterpart, and `<html>` carries a fixed `class="dark"` so the `dark:` rules inside the shadcn primitives resolve.

This is a deliberate limit, not an oversight. The translucent surfaces in section 5 depend on a dark backdrop: white at 7% over a dark canvas reads as glass, and over a light canvas it reads as nothing at all. A light theme would need its own surface recipe, not an inversion of this one. The tokens are structured so that adding one later means adding a `:root:not(.dark)` block, with no change to any component.

## 3. Colour

Every colour below is written in `oklch` in the stylesheet. The hex column is the same colour, for eyedropper work.

| Token                  | Hex       | Contrast on background | Use                                                                   |
| ---------------------- | --------- | ---------------------- | --------------------------------------------------------------------- |
| `--background`         | `#090A10` | —                      | The canvas. Never used on text.                                       |
| `--foreground`         | `#e3e1e9` | 15.3:1                 | Body and heading text.                                                |
| `--card`               | `#0d121f` | —                      | Solid surface, where glass is not wanted.                             |
| `--primary`            | `#4cd7f6` | 11.6:1                 | The brand cyan: actions, focus rings, active state.                   |
| `--primary-foreground` | `#040814` | —                      | Text on top of primary.                                               |
| `--success`            | `#4edea3` | 11.6:1                 | Mastery, correct answers, progress that went well.                    |
| `--highlight`          | `#d0bcff` | 11.6:1                 | Secondary accent: AI surfaces and the wordmark gradient.              |
| `--streak`             | `#f97316` | 7.1:1                  | Streaks and daily goals only. Loud on purpose, so rare.               |
| `--destructive`        | `#fb7185` | 7.3:1                  | Errors, refusals, destructive actions.                                |
| `--muted-foreground`   | `#9ba6b2` | 8.0:1                  | Supporting text, hints, placeholders.                                 |
| `--border`             | white 12% | —                      | Decorative edges: cards, dividers.                                    |
| `--input`              | white 28% | —                      | Form field edges. Stronger than `--border` on purpose; see section 8. |

Cyan carries the interface. Mint, lavender and orange are accents with one job each — a screen using all of them at once is a screen doing too much.

## 4. Typography

Two faces, chosen so each character comes from a face that has it:

- **Plus Jakarta Sans** for Latin text and numbers.
- **IBM Plex Sans Thai Looped** for Thai.

`--font-sans` lists them in that order. The browser takes each character from the first face that has the glyph, so Latin renders in Plus Jakarta Sans and Thai falls through to IBM Plex Sans Thai Looped automatically. No component needs to know which language it is rendering.

Looped Thai letterforms were chosen over loopless ones because the interface is Thai and loops are easier to read at small sizes and over long passages — which is the whole of a learning app.

Weight carries hierarchy more than size does: `font-bold` and `font-extrabold` for headings and actions, `font-semibold` for labels, normal for body.

## 5. Surfaces

Three materials, in `@layer components`:

| Class           | What it is                                                        | Use for                            |
| --------------- | ----------------------------------------------------------------- | ---------------------------------- |
| `.glass-panel`  | Translucent gradient, 20px blur, 180% saturation, inset highlight | Cards, dialogs, anything floating  |
| `.glass-pill`   | The same material at chip size                                    | Badges, counters, status chips     |
| `.cta-gradient` | Cyan → sky → indigo → purple, with a near-black label             | The one forward action on a screen |

The body carries two large, very soft radial lights. They are not decoration: glass needs an uneven backdrop to refract, and over a flat black the panels would read as grey rectangles.

Use glass for what floats and solid `--card` for what sits still. A screen where everything is glass has no depth at all.

`.cta-gradient` is available as the `cta` variant on `Button`. At most one per screen — a second one means neither is the main action.

## 6. Motion

Presses and hovers use a spring, `--ease-spring`, `cubic-bezier(0.34, 1.56, 0.64, 1)`, which overshoots slightly and settles. `.interactive-card` applies the standard treatment: lift on hover, shrink on press.

Everything in this section is suspended under `prefers-reduced-motion: reduce`, where transforms stop and only colour transitions remain. Any new animation gets the same treatment in the same commit.

## 7. Layout and breakpoints

LanguZe is a responsive web app: one interface that works on a phone, a tablet and a desktop browser (SRS section 2.3, NFR-014). It is not a phone app with a desktop fallback.

Tailwind's default breakpoints apply: `sm` 40rem, `md` 48rem, `lg` 64rem, `xl` 80rem.

Rules for new screens:

- **Design the narrow case first**, then let content spread. A layout that only works above `md` is not finished.
- **Single centred column** for focused tasks — signing in, one game question. Constrain with `max-w-sm` to `max-w-md` and centre it; the whitespace either side on a desktop is correct, not a gap to fill.
- **Content that scales** — photo grids, word lists, progress — spreads with a responsive column count rather than stretching one column to the full width of a monitor.
- **Interactive targets are at least 44px tall on a phone.** `Button` has the `xl` size (`h-11`) for this, and form fields are `h-11`. Smaller sizes are for dense desktop-only surfaces such as an admin table.
- **No horizontal scrolling at 320px.**

Navigation is deliberately not part of this document yet: the screens it would connect do not exist. When it arrives it will differ by width — a bottom bar is a phone pattern and does not belong on a desktop browser.

## 8. Accessibility rules the theme must keep

These were checked when the theme was built and must survive changes to it:

- **Text contrast.** Every foreground token clears 6.6:1 against the background — comfortably past the 4.5:1 that WCAG AA asks for body text. A new colour is measured before it is added.
- **Input edges reach 3:1.** `--input` is white at 28% rather than the 12% used for decorative borders, because a form field boundary is a meaningful control edge (WCAG 1.4.11). Do not "tidy" it back to `--border`.
- **Pinch-zoom stays enabled.** No `user-scalable=no` in the viewport meta (WCAG 1.4.4).
- **Scrollbars stay visible.** They are how a mouse user knows there is more.
- **Focus is always visible.** Focus rings use `--ring` at full strength; never remove an outline without replacing it.
- **Motion can be turned off.** See section 6.

## 9. What was not copied from the source design

The theme is adapted from a design prototype. Three things in it were left behind on purpose, each recorded so nobody restores them thinking they were missed: `user-scalable=no`, which blocks pinch-zoom; a global rule hiding every scrollbar; and `!important` on every rule, which was a generator artifact and is unnecessary once the values are real tokens.

The prototype also loaded a second display font that no rule used, and Google's Material Symbols icon font. LanguZe uses `lucide-react`, which is already a dependency and ships tree-shaken SVGs rather than a blocking font request.
