import { Injectable } from '@nestjs/common';
import type { TutorTool } from '../ai/ai-provider.js';
import { PrismaService } from '../prisma/prisma.service.js';
import { normalizeAnswer } from '../vocabulary/extraction-rules.js';

/** The most rows any tool will hand back, however many the model asks for. */
export const MOST_ROWS = 20;
const DEFAULT_ROWS = 10;

/** What a tool says when the model asks for something it cannot have. */
export interface ToolRefusal {
  error: string;
}

/**
 * A count the model asked for, made safe. Anything unusable becomes the default
 * rather than an error: the model guessing badly at a number is not a reason to
 * refuse the learner an answer.
 */
function rowsFrom(value: unknown): number {
  const asked = typeof value === 'number' ? Math.floor(value) : Number.NaN;
  if (!Number.isFinite(asked) || asked < 1) return DEFAULT_ROWS;
  return Math.min(asked, MOST_ROWS);
}

/**
 * What the tutor may look up (FR-072, AI tutor flow section 2).
 *
 * Every tool is built for one learner and closes over their id, so none of them
 * takes a learner identifier and the model has no way to name one. That is the
 * whole of the authorization: there is no query the model could write that would
 * reach another learner's data, because it never supplies the identifier.
 *
 * Nothing here writes. The tutor can read a learner's words, mistakes, and
 * progress, and can change none of them (FR-072).
 *
 * What comes back is data about the learner, including text they typed
 * themselves. It travels to the model as a tool result — quoted data, never
 * instructions (AIR-002).
 */
@Injectable()
export class TutorToolsService {
  constructor(private readonly prisma: PrismaService) {}

  /** The three tools, bound to this learner and to nobody else. */
  forLearner(learnerId: string): TutorTool[] {
    return [
      {
        name: 'weak_words',
        description:
          "The learner's words that are not yet mastered, the least known first, with how often they have got each one wrong.",
        parameters: [
          {
            name: 'count',
            type: 'integer',
            description: `How many words to return, 1 to ${MOST_ROWS}.`,
          },
        ],
        run: (input) => this.weakWords(learnerId, rowsFrom(input.count)),
      },
      {
        name: 'recent_mistakes',
        description:
          'The answers the learner has recently got wrong, newest first, with what they typed.',
        parameters: [
          {
            name: 'count',
            type: 'integer',
            description: `How many mistakes to return, 1 to ${MOST_ROWS}.`,
          },
        ],
        run: (input) => this.recentMistakes(learnerId, rowsFrom(input.count)),
      },
      {
        name: 'word_details',
        description:
          "One of the learner's own words: its meaning, an example, and how they have answered it.",
        parameters: [
          {
            name: 'word',
            type: 'string',
            description: 'The English word to look up.',
          },
        ],
        run: (input) => this.wordDetails(learnerId, input.word),
      },
    ];
  }

  /** Words still being learned, least known first (FR-072). */
  private async weakWords(learnerId: string, count: number) {
    const mastery = await this.prisma.wordMastery.findMany({
      where: { learnerId, level: { in: ['LEARNING', 'FAMILIAR'] } },
      orderBy: [{ level: 'asc' }, { lastPractisedAt: 'asc' }],
      take: count,
      include: {
        vocabularyWord: {
          select: {
            english: true,
            thaiMeaning: true,
            _count: { select: { attempts: { where: { isCorrect: false } } } },
          },
        },
      },
    });

    return {
      words: mastery.map((row) => ({
        english: row.vocabularyWord.english,
        thaiMeaning: row.vocabularyWord.thaiMeaning,
        mastery: row.level,
        mistakes: row.vocabularyWord._count.attempts,
        lastPractisedAt: row.lastPractisedAt.toISOString(),
      })),
    };
  }

  /** What the learner recently got wrong, and what they actually typed (FR-042). */
  private async recentMistakes(learnerId: string, count: number) {
    const attempts = await this.prisma.attempt.findMany({
      where: { learnerId, isCorrect: false },
      orderBy: { answeredAt: 'desc' },
      take: count,
      include: {
        vocabularyWord: { select: { english: true, thaiMeaning: true } },
      },
    });

    return {
      mistakes: attempts.map((attempt) => ({
        english: attempt.vocabularyWord.english,
        thaiMeaning: attempt.vocabularyWord.thaiMeaning,
        // "I don't know" is not a wrong answer, so it is reported as its own thing.
        answered: attempt.isDontKnow ? null : attempt.answerText,
        saidTheyDidNotKnow: attempt.isDontKnow,
        answeredAt: attempt.answeredAt.toISOString(),
      })),
    };
  }

  /**
   * One word of the learner's. The name is matched the way answers are (SRS 4.2),
   * so a model writing "the sofa" finds the word the learner knows as `sofa`.
   */
  private async wordDetails(learnerId: string, asked: unknown) {
    if (typeof asked !== 'string' || asked.trim() === '') {
      return {
        error: 'Give the English word to look up.',
      } satisfies ToolRefusal;
    }

    const word = await this.prisma.vocabularyWord.findFirst({
      where: { learnerId, english: normalizeAnswer(asked) },
      include: {
        mastery: true,
        occurrences: {
          orderBy: { createdAt: 'desc' },
          take: 1,
          select: { exampleSentence: true, cefrLevel: true },
        },
        attempts: {
          orderBy: { answeredAt: 'desc' },
          take: 5,
          select: {
            isCorrect: true,
            isDontKnow: true,
            answerText: true,
            answeredAt: true,
          },
        },
      },
    });

    /*
     * A word the learner does not have is not an error to hide: saying so is what
     * stops the tutor inventing a history for it (FR-075, US-071 criterion 3).
     */
    if (!word) {
      return {
        found: false,
        error: 'The learner does not have this word.',
      };
    }

    const occurrence = word.occurrences[0];
    return {
      found: true,
      english: word.english,
      thaiMeaning: word.thaiMeaning,
      exampleSentence: occurrence?.exampleSentence ?? null,
      cefrLevel: occurrence?.cefrLevel ?? null,
      mastery: word.mastery?.level ?? 'NEW',
      recentAnswers: word.attempts.map((attempt) => ({
        correct: attempt.isCorrect,
        answered: attempt.isDontKnow ? null : attempt.answerText,
        saidTheyDidNotKnow: attempt.isDontKnow,
        answeredAt: attempt.answeredAt.toISOString(),
      })),
    };
  }
}
