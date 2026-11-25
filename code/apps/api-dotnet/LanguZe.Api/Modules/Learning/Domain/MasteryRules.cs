namespace LanguZe.Api.Modules.Learning.Domain;

/// <summary>A word's level; the database's <c>mastery_level</c>, kept here so the rules need no database (SRS 4.1).</summary>
public enum Stage
{
    Learning,
    Familiar,
    Mastered,
}

/// <summary>What LanguZe knows about one word before an attempt (SRS 4.1). None is <c>NEW</c>: a word nobody has answered has no mastery.</summary>
/// <param name="Streak">Correct answers in a row at this level.</param>
/// <param name="FamiliarOn">The Asia/Bangkok day the word last became <c>FAMILIAR</c>.</param>
public sealed record Mastery(Stage Level, int Streak, DateOnly? FamiliarOn);

/// <summary>
/// The word's mastery after one attempt (mastery-rules.ts; FR-041, SRS 4.1): a pure function of
/// what was known, whether the answer was right, and today's day in Bangkok, so every case of
/// the rule is tested without a database. The day is the learner's (V1), because a
/// <c>FAMILIAR</c> word cannot be mastered twice in one of their days.
/// </summary>
public static class MasteryRules
{
    /// <summary>Correct answers in a row needed to move up a level (SRS 4.1).</summary>
    public const int StreakToAdvance = 2;

    public static Mastery Next(Mastery? current, bool correct, DateOnly today)
    {
        // NEW: the first attempt starts the word off, right or wrong.
        if (current is null)
        {
            return new Mastery(Stage.Learning, correct ? 1 : 0, null);
        }

        if (!correct)
        {
            return AfterMistake(current, today);
        }

        return current.Level switch
        {
            Stage.Learning => AdvanceOr(Stage.Familiar, current, today),

            // A word becomes FAMILIAR on a day the learner got it right. Answering it right again
            // the same day says nothing about tomorrow, so only later days count (US-040 #4).
            Stage.Familiar => current.FamiliarOn == today ? current : AdvanceOr(Stage.Mastered, current, today),

            // A mastered word stays mastered while the answers keep coming.
            _ => current,
        };
    }

    /// <summary>
    /// One more correct answer, and the next level once the streak is long enough. The streak
    /// resets on the way up; a <c>MASTERED</c> word never reads its streak.
    /// </summary>
    private static Mastery AdvanceOr(Stage next, Mastery current, DateOnly today)
    {
        var streak = current.Streak + 1;
        if (streak < StreakToAdvance)
        {
            return current with { Streak = streak };
        }

        // The familiar day moves only when the word arrives at FAMILIAR.
        return new Mastery(next, 0, next == Stage.Familiar ? today : current.FamiliarOn);
    }

    /// <summary>A wrong answer drops a word one level at most, and never past <c>LEARNING</c>.</summary>
    private static Mastery AfterMistake(Mastery current, DateOnly today) =>
        current.Level == Stage.Mastered
            ? new Mastery(Stage.Familiar, 0, today)
            : new Mastery(Stage.Learning, 0, current.FamiliarOn);
}
