import type { CefrLevel } from '../generated/prisma/enums.js';

/** What an AI provider offers for one object it found in a photo. */
export interface ExtractedItem {
  english: string;
  thaiMeaning: string;
  exampleSentence: string;
  cefrLevel: string;
  acceptedVariants: string[];
  box: { x: number; y: number; width: number; height: number };
}

/** One item that passed every rule and is ready to be saved. */
export interface CheckedItem extends ExtractedItem {
  cefrLevel: CefrLevel;
  thaiMeaningKey: string;
}

/** A world keeps at most this many words, the clearest objects first (FR-024). */
export const MAX_WORDS_PER_WORLD = 12;

/** Below this, the photo is not worth learning from and the analysis fails (FR-024). */
export const MIN_WORDS_PER_WORLD = 3;

const MAX_ENGLISH_LENGTH = 40;
const MAX_THAI_LENGTH = 100;
const MAX_SENTENCE_LENGTH = 200;
const MAX_VARIANTS = 10;

const CEFR_LEVELS = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'];
/** Letters, spaces and hyphens only: "coffee table" and "T-shirt" pass, "sofa!" does not. */
const ENGLISH_WORD = /^[a-z]+(?:[ -][a-z]+)*$/;
const THAI_SCRIPT = /[฀-๿]/;

/**
 * Words for people and parts of the body, which extraction must never return
 * (FR-094, AIR-009). The prompt says so as well; this is the part that holds when
 * the model ignores it, because a photo with a person in it is allowed and only the
 * words about them are not.
 */
const PEOPLE_AND_BODY = new Set([
  'man',
  'woman',
  'boy',
  'girl',
  'child',
  'baby',
  'person',
  'people',
  'lady',
  'gentleman',
  'face',
  'head',
  'hair',
  'eye',
  'eyebrow',
  'ear',
  'nose',
  'mouth',
  'lip',
  'tooth',
  'tongue',
  'chin',
  'cheek',
  'neck',
  'shoulder',
  'arm',
  'elbow',
  'hand',
  'finger',
  'thumb',
  'chest',
  'stomach',
  'back',
  'waist',
  'hip',
  'leg',
  'knee',
  'foot',
  'toe',
  'skin',
  'body',
]);

/**
 * The normalization of SRS 4.2, used for comparing answers and for deciding when two
 * words are the same: spaces collapsed, lower case, one leading article removed.
 */
export function normalizeAnswer(value: string): string {
  const collapsed = value.trim().replace(/\s+/g, ' ').toLowerCase();
  return collapsed.replace(/^(?:a|an|the) /, '');
}

/** Two spellings of one Thai meaning are one word, so identity ignores spacing. */
export const thaiMeaningKeyOf = (meaning: string): string =>
  meaning.trim().replace(/\s+/g, ' ');

/**
 * Whether one item may be saved (AIR-003, AIR-009). An item that fails is dropped
 * and the rest of the response is kept (FR-023): one bad object out of eight is not
 * a reason to lose the photo's other words.
 */
export function checkItem(item: ExtractedItem): CheckedItem | null {
  const english = normalizeAnswer(item.english);
  if (!english || english.length > MAX_ENGLISH_LENGTH) return null;
  if (!ENGLISH_WORD.test(english)) return null;
  if (PEOPLE_AND_BODY.has(english)) return null;

  const thaiMeaning = item.thaiMeaning.trim();
  if (!thaiMeaning || thaiMeaning.length > MAX_THAI_LENGTH) return null;
  if (!THAI_SCRIPT.test(thaiMeaning)) return null;

  const exampleSentence = item.exampleSentence.trim();
  if (!exampleSentence || exampleSentence.length > MAX_SENTENCE_LENGTH) {
    return null;
  }

  const variants = acceptedVariantsOf(item.acceptedVariants, english);
  // The sentence has to teach the word, so it must contain it or one of its variants.
  if (!mentions(exampleSentence, [english, ...variants])) return null;

  if (!CEFR_LEVELS.includes(item.cefrLevel)) return null;
  if (!isInsidePhoto(item.box)) return null;

  return {
    ...item,
    english,
    thaiMeaning,
    thaiMeaningKey: thaiMeaningKeyOf(thaiMeaning),
    exampleSentence,
    cefrLevel: item.cefrLevel as CefrLevel,
    acceptedVariants: variants,
  };
}

/**
 * The items to save, in the provider's own order of confidence: each one checked,
 * duplicates of the same word merged so one box remains, and at most twelve
 * (AIR-004, FR-024).
 */
export function checkedItems(items: ExtractedItem[]): CheckedItem[] {
  const kept = new Map<string, CheckedItem>();

  for (const item of items) {
    const checked = checkItem(item);
    if (!checked) continue;

    // The first box wins: the provider lists what it is surest of first.
    const identity = `${checked.english}|${checked.thaiMeaningKey}`;
    if (!kept.has(identity)) kept.set(identity, checked);
    if (kept.size === MAX_WORDS_PER_WORLD) break;
  }

  return [...kept.values()];
}

/** Normalized, unique, never the word itself, and at most ten (AIR-003, V19). */
function acceptedVariantsOf(variants: string[], english: string): string[] {
  const kept = new Set<string>();
  for (const variant of variants) {
    const normalized = normalizeAnswer(variant);
    if (!normalized || normalized === english) continue;
    if (!ENGLISH_WORD.test(normalized)) continue;
    if (normalized.length > MAX_ENGLISH_LENGTH) continue;
    kept.add(normalized);
    if (kept.size === MAX_VARIANTS) break;
  }
  return [...kept];
}

/**
 * A word and its regular plurals: sofa, sofas; box, boxes; battery, batteries.
 * In a phrase only the last word takes the ending: coffee tables. An irregular
 * plural such as knives is not guessed; a model can give it as a variant.
 */
function formsOf(word: string): string[] {
  const forms = [word, `${word}s`, `${word}es`];
  if (/[^aeiou]y$/.test(word)) forms.push(`${word.slice(0, -1)}ies`);
  return forms;
}

/**
 * Whether the sentence uses one of these words, rather than merely containing the
 * letters: "car" is not in "carpet". A regular plural counts as the word. The
 * sentence is only shown to the learner, never blanked out for an answer, and
 * "The bathroom has white tiles." teaches tile as well as a singular would —
 * refusing it lost three words of eleven in the AI evaluation.
 */
function mentions(sentence: string, words: string[]): boolean {
  const normalized = ` ${sentence
    .toLowerCase()
    .replace(/[^a-z -]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()} `;
  return words
    .flatMap(formsOf)
    .some((form) => normalized.includes(` ${form} `));
}

/** A box has a real size and lies inside the photo, in 0–1 coordinates (AIR-003). */
function isInsidePhoto(box: ExtractedItem['box']): boolean {
  const { x, y, width, height } = box;
  if (![x, y, width, height].every(Number.isFinite)) return false;
  if (width <= 0 || height <= 0) return false;
  return x >= 0 && y >= 0 && x + width <= 1 && y + height <= 1;
}
