# Accessibility review for Release 1.0

- **Status:** Done in code (2026-10-07); large text and reflow checked (2026-10-08), and reflow fixed on the create-world page (A7, 2026-10-26); the keyboard and the in-app browsers checked (2026-10-26); VoiceOver passed once A6 was fixed (2026-10-26); TalkBack accepted without an Android device (section 4)
- **Requirement:** [NFR-012](../requirements/SRS.md): the core journeys meet WCAG 2.2 level AA, being keyboard operable, with visible focus, sufficient contrast and labelled inputs, and with a highlight box that does not rely on colour alone
- **Used by:** [launch checklist, section 5](../deployment/launch-checklist.md#5-reviews)

## Scope and method

**Scope.** The learner's core journeys:

- sign-up, sign-in and the Terms step
- email verification and password reset
- the worlds: list, new, and one world with its words
- a game, review, progress, the tutor, and the account page
- the legal pages

The admin area is not a core journey and was not reviewed.

**How it was checked:**

1. **Lint.** `eslint-plugin-jsx-a11y`, which `eslint-config-next` includes, runs on every commit.
2. **axe-core in Vitest.** `src/test/axe.ts` runs axe-core 4.14 against the WCAG 2.0 to 2.2 A and AA rules and the best practices. It covers every learner page, plus each state a learner reaches by pressing something: a form with its errors, the feedback after an answer, and each confirmation. jsdom lays nothing out, so the rules that measure the screen are left to points 3 and 4. A test proves the helper fails on a broken page.
3. **Palette contrast.** `src/app/palette.test.ts` reads the colours from `globals.css` and works out WCAG's contrast ratio for every pair the interface uses (31 checks).
4. **Code review** against each WCAG 2.2 AA criterion, as recorded in section 3.

What cannot be checked this way, listed in section 4: screen readers, zoom and reflow, and sizes as a phone lays them out.

## 1. Findings and fixes

| #   | WCAG         | Finding                                                                                                                                                                                                                                                                                                                   | Fix                                                                                                                                                                                                                     |
| --- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A1  | 1.4.11       | The edge of a field, `--input` at 28% white, reached only 2.4:1 against the page, though its own comment asked for 3:1                                                                                                                                                                                                    | Raised to 38%: at least 3.5:1 on the page, the card and the glass panels                                                                                                                                                |
| A2  | 1.4.11       | The game's answer field and the photo picker were drawn with `--border` (1.3:1)                                                                                                                                                                                                                                           | Both use `--input`                                                                                                                                                                                                      |
| A3  | 3.1.2        | Nothing was marked as English, so a screen reader set to Thai reads "sofa" with Thai sounds, the one thing a vocabulary learner must not hear                                                                                                                                                                             | `lang="en"` on the word and its example in the world and in the game feedback, on the answer field, on the game result, and on the word in the removal question                                                         |
| A4  | 2.4.3        | Focus was lost whenever a step replaced the button pressed: answering a question, removing a word, renaming or deleting a world, and deleting the account. A keyboard learner was sent back to the top of the page                                                                                                        | Focus moves into the step, onto "ข้อต่อไป", the new name or "ยกเลิก", and comes back to the button that opened the step when it closes                                                                                  |
| A5  | 3.2.3, 3.2.4 | Found on a phone by the operator: My worlds was the one page of the home menu with no way back to it, and the account page named the same link "← กลับหน้าแรก" where the others say "← หน้าแรก"                                                                                                                           | My worlds has the link; every page now names it "← หน้าแรก"                                                                                                                                                             |
| A6  | 3.1.2, 4.1.3 | Found with VoiceOver on an iPhone by the operator: the whole answer feedback was a live region, and VoiceOver read it out as one string, speaking the word, its example and the learner's answer with Thai sounds despite `lang="en"`. On a world's page, where nothing is announced, the same words were read in English | Only the verdict, all Thai, is announced. The English is read an element at a time, in an English voice, as the learner moves back from "ข้อต่อไป". "I don't know" now says "ไม่เป็นไร", with "คำนี้คือ" above the word |
| A7  | 1.4.10       | Found on an iPhone by the operator: on the create-world page, the photo picker Safari draws widened the form's column, and the name field and the button ran past the edge. The reflow check of 2026-10-08 used a desktop browser's device emulation, not Safari on a phone                                               | Every grid on the page has one column that can shrink, and the picker is held to its width                                                                                                                              |

Two bugs that are not about accessibility were found along the way and fixed in their own commits:

- **B1.** The sign-up form and the Terms step linked to `/terms` and `/privacy`. The first is the Terms step itself; the second does not exist. Both now go to `/terms-of-use` and `/privacy-policy` (FR-090).
- **B2.** The account deletion warning listed Google and LINE sign-ins but not Facebook.

## 2. What was already right

- **Highlight box (NFR-012).** It does not rely on colour. In a game the box is a white outline inside a dark one, which shows on any photo. In a world each box carries a number that matches the word list.
- **Forms.** Every field has a label. Errors are tied to their field with `aria-describedby`, announced with `role="alert"`, and the field is marked `aria-invalid`.
- **Status messages.** The analysis progress, the game feedback and the tutor's streamed reply are live regions.
- **Feedback.** It never relies on colour: a mark and words say right or wrong.
- **Pages.** Every page has a title, a `main` landmark and one `h1`. `<html lang="th">` is set, and motion respects `prefers-reduced-motion`.
- **Touch targets.** The main buttons are 44 px tall.

## 3. WCAG 2.2 AA, criterion by criterion

| Criterion                        | Result         | Basis                                                                                                                                                                            |
| -------------------------------- | -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1.1.1 Non-text content           | Pass           | Photos have `alt`; boxes are hidden from assistive technology because the numbered list says the same; decorative marks are `aria-hidden`; axe                                   |
| 1.3.1 Info and relationships     | Pass           | Labels, `aria-describedby`, lists and headings; axe                                                                                                                              |
| 1.3.2 Meaningful sequence        | Pass           | Reading order is the visual order                                                                                                                                                |
| 1.3.4 Orientation                | Pass           | Nothing locks the orientation                                                                                                                                                    |
| 1.3.5 Identify input purpose     | Pass           | `autocomplete`: `email`, `current-password`, `new-password`, `nickname`                                                                                                          |
| 1.4.1 Use of colour              | Pass           | Section 2                                                                                                                                                                        |
| 1.4.3 Contrast (minimum)         | Pass           | Palette test: every text colour at least 6.1:1, the CTA's text at least 6.7:1                                                                                                    |
| 1.4.4 Resize text                | Pass           | Desktop browser zoomed in: text and buttons grow without clipping or overlap (2026-10-08)                                                                                        |
| 1.4.5 Images of text             | Pass           | None                                                                                                                                                                             |
| 1.4.10 Reflow                    | Pass           | 320 × 568 CSS px: the home page, sign-up, a world with its words, a game question and both kinds of feedback, and the tutor; no sideways scrolling, nothing cut off (2026-10-08) |
| 1.4.11 Non-text contrast         | Fixed (A1, A2) | Palette test: field edges at least 3.5:1, the focus ring at least 3.4:1                                                                                                          |
| 1.4.12 Text spacing              | On a phone     | No fixed-height text box is known, but it was not tried                                                                                                                          |
| 1.4.13 Content on hover or focus | Pass           | Hovering a word only recolours its box                                                                                                                                           |
| 2.1.1 Keyboard                   | Pass           | Native buttons, links and fields throughout; in the tutor Enter sends and Shift+Enter starts a new line                                                                          |
| 2.1.2 No keyboard trap           | Pass           | No dialogs or custom widgets that hold focus                                                                                                                                     |
| 2.1.4 Character key shortcuts    | Pass           | None                                                                                                                                                                             |
| 2.2.1 Timing adjustable          | Pass           | No time limit on any input; a game waits 24 hours (V17)                                                                                                                          |
| 2.2.2 Pause, stop, hide          | Pass           | No moving content besides waiting indicators                                                                                                                                     |
| 2.3.1 Three flashes              | Pass           | None                                                                                                                                                                             |
| 2.4.1 Bypass blocks              | Pass           | No navigation repeats before the content; every page has `main`                                                                                                                  |
| 2.4.2 Page titled                | Pass           | Every page sets its title                                                                                                                                                        |
| 2.4.3 Focus order                | Fixed (A4)     | Component tests check where focus goes                                                                                                                                           |
| 2.4.4 Link purpose               | Pass           | Links are named by their text, and after B1 they go where they say                                                                                                               |
| 2.4.5 Multiple ways              | Pass           | The home page links every section; each page also has its own address                                                                                                            |
| 2.4.6 Headings and labels        | Pass           | Headings and labels say what follows                                                                                                                                             |
| 2.4.7 Focus visible              | Pass           | Every control draws a focus ring; to be seen on a phone and a desktop (section 4)                                                                                                |
| 2.4.11 Focus not obscured        | Pass           | No sticky or fixed elements                                                                                                                                                      |
| 2.5.1 Pointer gestures           | Pass           | Single taps only                                                                                                                                                                 |
| 2.5.2 Pointer cancellation       | Pass           | Native click, which acts on release                                                                                                                                              |
| 2.5.3 Label in name              | Pass           | A name that differs from the text contains it, as "ลบคำว่า sofa" contains "ลบ"                                                                                                   |
| 2.5.4 Motion actuation           | Pass           | None                                                                                                                                                                             |
| 2.5.7 Dragging movements         | Pass           | None                                                                                                                                                                             |
| 2.5.8 Target size (minimum)      | Pass in code   | Main buttons 44 px, small ones 28 px, footer links spaced as the exception allows; to be confirmed on a phone                                                                    |
| 3.1.1 Language of page           | Pass           | `lang="th"`                                                                                                                                                                      |
| 3.1.2 Language of parts          | Fixed (A3)     | Except the tutor's replies (section 5)                                                                                                                                           |
| 3.2.1 On focus, 3.2.2 On input   | Pass           | Nothing changes context on focus or input                                                                                                                                        |
| 3.2.3, 3.2.4 Consistency         | Fixed (A5)     | The same footer everywhere, and the same "← หน้าแรก" link on every page of the home menu                                                                                         |
| 3.2.6 Consistent help            | Pass           | The contact page is linked from the footer of every page                                                                                                                         |
| 3.3.1 Error identification       | Pass           | Each error is named in text, next to its field                                                                                                                                   |
| 3.3.2 Labels or instructions     | Pass           | Hints such as the photo types and size                                                                                                                                           |
| 3.3.3 Error suggestion           | Pass           | Messages say what to change                                                                                                                                                      |
| 3.3.4 Error prevention           | Pass           | Deleting a word, a world or the account asks first                                                                                                                               |
| 3.3.7 Redundant entry            | Pass           | Nothing is asked twice, except the new password, which security allows                                                                                                           |
| 3.3.8 Accessible authentication  | Pass           | No puzzle or memory test; pasting and password managers work                                                                                                                     |
| 4.1.2 Name, role, value          | Pass           | Native elements; axe                                                                                                                                                             |
| 4.1.3 Status messages            | Pass           | `role="status"`, `role="alert"` and `aria-live`                                                                                                                                  |

## 4. To check on a real phone

Device emulators that load the page inside a frame block its cookies, so signing in with Google, LINE or Facebook fails there with `state_mismatch` or a provider error. That is the tool, not LanguZe: check sign-in in a normal tab, in the browser's own device mode, or on the phone.

Owner: you, during your own run-through ([launch checklist, section 4](../deployment/launch-checklist.md#4-production-configuration-and-checks)). How to run each check, and where to record the result: [accessibility checks on real devices](accessibility-checks.md).

1. **Screen readers.** VoiceOver on an iPhone (Safari) and TalkBack on Android (Chrome), through sign-up, creating a world, a game, a review and the tutor. Each step can be done, and English words are read in English. VoiceOver was run on 2026-10-26: everything passed but the game feedback (A6), and once that was fixed, it passed too. TalkBack was not run, as no Android device was at hand, which was accepted for Release 1.0 on 2026-10-26: both screen readers work from the same names, roles, languages and announcements, which axe-core checks in code, so VoiceOver finds most of what TalkBack would. Run TalkBack when an Android device is at hand, or when a tester uses one
2. ~~**Large text and a narrow screen.** Text at 200%, and a width of 320 CSS pixels: nothing cut off or overlapping, and no sideways scrolling (1.4.4, 1.4.10)~~ Passed on 2026-10-08, in a desktop browser and at 320 × 568 in device emulation. On a real iPhone the create-world page still ran past the edge (A7), fixed and checked on 2026-10-26
3. ~~**Keyboard alone on a desktop browser.** From sign-in to a world to a game, with the focus ring visible at every step~~ Passed on 2026-10-26, in a desktop browser
4. ~~**The LINE and Facebook in-app browsers**, as in NFR-018~~ Passed on an iPhone: LINE and Messenger on 2026-10-21, a link in a Facebook post on 2026-10-26

## 5. Known limitations

- **The tutor's replies** mix Thai and English in free text, so English inside them is not marked and may be read with Thai sounds. Marking it would mean the model labelling its own English.
- **The admin area** was not reviewed.
