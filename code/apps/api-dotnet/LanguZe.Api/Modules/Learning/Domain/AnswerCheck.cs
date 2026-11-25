using System.Globalization;
using System.Text;
using LanguZe.Api.Modules.Vocabulary.Contracts;

namespace LanguZe.Api.Modules.Learning.Domain;

/// <summary>
/// Whether what the learner typed names the word (answer-check.ts; FR-032, SRS 4.2). Both sides
/// go through the normalization the word was stored with, which is why this uses the vocabulary
/// rule rather than a copy of it. Typos are not accepted.
/// </summary>
public static class AnswerCheck
{
    /// <summary>The longest answer a learner may type (FR-032, V19).</summary>
    public const int MaxAnswerLength = 100;

    /// <summary>What one correct answer is worth, in a game or a review (FR-060).</summary>
    public const int XpForCorrect = 10;

    public static bool IsCorrectAnswer(string typed, string word, IReadOnlyList<string> acceptedVariants)
    {
        var answer = AnswerOf(typed);

        // An empty answer is never right. The API refuses one before it gets here, but an
        // answer of nothing but invisible characters still arrives empty.
        if (answer.Length == 0)
        {
            return false;
        }

        return new[] { word }.Concat(acceptedVariants).Any(accepted => ExtractionRules.NormalizeAnswer(accepted) == answer);
    }

    /// <summary>Whether the learner typed the word itself rather than one of its variants (FR-033).</summary>
    public static bool IsTheWord(string typed, string word) => AnswerOf(typed) == ExtractionRules.NormalizeAnswer(word);

    /// <summary>XP for one attempt. An incorrect answer earns nothing and costs nothing (V4).</summary>
    public static int XpFor(bool correct) => correct ? XpForCorrect : 0;

    /// <summary>
    /// The learner's answer as they see it, then normalized like the word (FR-032). Characters a
    /// learner can type without seeing them go first: combining marks with nothing to sit on and
    /// formatting characters such as a zero-width space, as JavaScript's <c>[\p{M}\p{Cf}]</c> with
    /// the <c>u</c> flag removes them, by code point. A Thai keyboard's <c>b</c> key types the vowel
    /// mark sara i (U+0E34). No accepted answer holds either kind (AIR-003), so this can only let
    /// through what the learner meant.
    /// </summary>
    private static string AnswerOf(string typed)
    {
        var visible = new StringBuilder(typed.Length);
        foreach (var rune in typed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format))
            {
                visible.Append(rune);
            }
        }

        return ExtractionRules.NormalizeAnswer(visible.ToString());
    }
}
