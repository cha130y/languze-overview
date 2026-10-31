import { describe, expect, it } from 'vitest';
import {
  MAX_WORDS_PER_WORLD,
  checkItem,
  checkedItems,
  normalizeAnswer,
  thaiMeaningKeyOf,
  type ExtractedItem,
} from './extraction-rules.js';

/** Distinct letter-only suffixes: a, b, c … for words that have to differ. */
const letters = (count: number) =>
  Array.from({ length: count }, (_, index) =>
    String.fromCharCode('a'.charCodeAt(0) + index),
  );

const item = (overrides: Partial<ExtractedItem> = {}): ExtractedItem => ({
  english: 'sofa',
  thaiMeaning: 'โซฟา',
  exampleSentence: 'I sit on the sofa.',
  cefrLevel: 'A1',
  acceptedVariants: ['couch'],
  box: { x: 0.1, y: 0.2, width: 0.3, height: 0.25 },
  ...overrides,
});

describe('normalizeAnswer (SRS 4.2)', () => {
  it.each([
    ['Sofa', 'sofa'],
    ['  a   sofa ', 'sofa'],
    ['The Coffee  Table', 'coffee table'],
    ['an apple', 'apple'],
    // Only a leading article goes; "the" inside the phrase is part of it.
    ['end of the day', 'end of the day'],
  ])('%s becomes %s', (given, expected) => {
    expect(normalizeAnswer(given)).toBe(expected);
  });
});

describe('thaiMeaningKeyOf', () => {
  it('ignores spacing so one meaning is one word', () => {
    expect(thaiMeaningKeyOf('  โซฟา   ยาว ')).toBe('โซฟา ยาว');
  });
});

describe('checking one item (AIR-003)', () => {
  it('keeps a good item, normalized', () => {
    const checked = checkItem(item({ english: ' A Sofa ' }));

    expect(checked?.english).toBe('sofa');
    expect(checked?.thaiMeaningKey).toBe('โซฟา');
    expect(checked?.cefrLevel).toBe('A1');
  });

  it.each([
    ['an empty word', { english: '   ' }],
    ['a word with digits', { english: 'sofa 2' }],
    ['a word with punctuation', { english: 'sofa!' }],
    ['a word over 40 characters', { english: 'a'.repeat(41) }],
    ['a meaning with no Thai script', { thaiMeaning: 'sofa' }],
    ['an empty meaning', { thaiMeaning: ' ' }],
    ['a meaning over 100 characters', { thaiMeaning: 'โซ'.repeat(60) }],
    ['a level outside A1–C2', { cefrLevel: 'D1' }],
    [
      'a sentence over 200 characters',
      { exampleSentence: `sofa ${'x '.repeat(120)}` },
    ],
  ])('refuses %s', (_case, overrides) => {
    expect(checkItem(item(overrides))).toBeNull();
  });

  /* The sentence has to teach the word, which is the whole point of showing it. */
  it('refuses a sentence that does not use the word', () => {
    expect(
      checkItem(item({ exampleSentence: 'This room is very tidy.' })),
    ).toBeNull();
  });

  it('accepts a sentence that uses a variant instead', () => {
    expect(
      checkItem(
        item({
          exampleSentence: 'The couch is green.',
          acceptedVariants: ['couch'],
        }),
      ),
    ).not.toBeNull();
  });

  it('does not mistake letters inside another word for the word', () => {
    expect(
      checkItem(
        item({ english: 'car', exampleSentence: 'The carpet is red.' }),
      ),
    ).toBeNull();
    expect(
      checkItem(item({ exampleSentence: 'We sat on the sofabed.' })),
    ).toBeNull();
  });

  /*
   * A regular plural is the word: the sentence is only shown, never blanked out.
   * Refusing "white tiles" for tile lost three words of eleven in the evaluation.
   */
  it.each([
    ['sofa', 'I like sofas.'],
    ['box', 'The boxes are full.'],
    ['battery', 'The batteries are new.'],
    ['toy', 'The toys are on the floor.'],
    ['coffee table', 'We have two coffee tables.'],
  ])(
    'accepts a sentence that uses %s in its regular plural',
    (english, exampleSentence) => {
      expect(checkItem(item({ english, exampleSentence }))).not.toBeNull();
    },
  );

  it('does not guess an irregular plural, which a variant can give instead', () => {
    expect(
      checkItem(
        item({ english: 'knife', exampleSentence: 'The knives are sharp.' }),
      ),
    ).toBeNull();
    expect(
      checkItem(
        item({
          english: 'knife',
          exampleSentence: 'The knives are sharp.',
          acceptedVariants: ['knives'],
        }),
      ),
    ).not.toBeNull();
  });

  /* FR-094, AIR-009: a photo may contain a person; their words may not be taught. */
  it.each(['man', 'woman', 'child', 'hand', 'face', 'hair'])(
    'refuses the word "%s"',
    (english) => {
      expect(
        checkItem(
          item({
            english,
            exampleSentence: `This is a ${english}.`,
            acceptedVariants: [],
          }),
        ),
      ).toBeNull();
    },
  );

  describe('the highlight box', () => {
    it.each([
      ['outside on the left', { x: -0.1, y: 0.1, width: 0.2, height: 0.2 }],
      ['past the right edge', { x: 0.9, y: 0.1, width: 0.2, height: 0.2 }],
      ['past the bottom', { x: 0.1, y: 0.95, width: 0.1, height: 0.2 }],
      ['with no width', { x: 0.1, y: 0.1, width: 0, height: 0.2 }],
      ['with a negative height', { x: 0.1, y: 0.1, width: 0.2, height: -0.2 }],
      ['not a number', { x: 0.1, y: 0.1, width: Number.NaN, height: 0.2 }],
    ])('refuses a box %s', (_case, box) => {
      expect(checkItem(item({ box }))).toBeNull();
    });

    it('accepts a box filling the whole photo', () => {
      expect(
        checkItem(item({ box: { x: 0, y: 0, width: 1, height: 1 } })),
      ).not.toBeNull();
    });
  });

  describe('accepted variants', () => {
    it('normalizes them and drops the word itself', () => {
      const checked = checkItem(
        item({ acceptedVariants: ['Couch', ' the sofa ', 'settee'] }),
      );

      expect(checked?.acceptedVariants).toEqual(['couch', 'settee']);
    });

    it('keeps at most ten, without duplicates', () => {
      // Letters only: a word with digits is refused by the rule above.
      const many = letters(15).map((suffix) => `couch${suffix}`);
      const checked = checkItem(
        item({ acceptedVariants: ['couch', 'couch', ...many] }),
      );

      expect(checked?.acceptedVariants).toHaveLength(10);
      expect(new Set(checked?.acceptedVariants).size).toBe(10);
    });

    it('drops a variant that is not a word', () => {
      const checked = checkItem(
        item({ acceptedVariants: ['couch', 'sofa!!'] }),
      );

      expect(checked?.acceptedVariants).toEqual(['couch']);
    });
  });
});

describe('checking a whole response (AIR-004, FR-023, FR-024)', () => {
  it('keeps the good items and drops the bad ones', () => {
    const checked = checkedItems([
      item(),
      item({ english: 'lamp!', thaiMeaning: 'โคมไฟ' }),
      item({
        english: 'table',
        thaiMeaning: 'โต๊ะ',
        exampleSentence: 'The table is round.',
      }),
    ]);

    expect(checked.map((word) => word.english)).toEqual(['sofa', 'table']);
  });

  /* AIR-004: one word, one box. The first is kept, being what the model was surest of. */
  it('merges a word that appears twice, keeping the first box', () => {
    const checked = checkedItems([
      item({ box: { x: 0.1, y: 0.1, width: 0.2, height: 0.2 } }),
      item({ box: { x: 0.5, y: 0.5, width: 0.2, height: 0.2 } }),
    ]);

    expect(checked).toHaveLength(1);
    expect(checked[0].box.x).toBe(0.1);
  });

  it('treats the same word with another meaning as its own word', () => {
    const checked = checkedItems([
      item({
        english: 'glass',
        thaiMeaning: 'แก้วน้ำ',
        exampleSentence: 'The glass is full.',
      }),
      item({
        english: 'glass',
        thaiMeaning: 'กระจก',
        exampleSentence: 'The glass is clean.',
      }),
    ]);

    expect(checked).toHaveLength(2);
  });

  it('keeps at most twelve words (FR-024)', () => {
    const many = letters(20).map((suffix) =>
      item({
        english: `object${suffix}`,
        thaiMeaning: `สิ่งของ${suffix}`,
        exampleSentence: `This is object${suffix}.`,
        acceptedVariants: [],
      }),
    );

    expect(checkedItems(many)).toHaveLength(MAX_WORDS_PER_WORLD);
  });

  it('gives nothing back for a response of only bad items', () => {
    expect(
      checkedItems([item({ english: '???' }), item({ cefrLevel: 'X' })]),
    ).toEqual([]);
  });
});
