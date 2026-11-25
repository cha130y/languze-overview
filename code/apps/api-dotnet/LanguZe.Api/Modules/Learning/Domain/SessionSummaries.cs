namespace LanguZe.Api.Modules.Learning.Domain;

/// <summary>One answer, as the summary needs to see it.</summary>
public sealed record SummarisedAttempt(bool IsCorrect, int XpAwarded, Stage? LevelBefore, Stage LevelAfter, string English);

/// <summary>A word whose level an answer changed, and where it ended (FR-035).</summary>
public sealed record LevelChange(string English, Stage Level);

/// <summary>What a learner achieved in a session (FR-035, US-033).</summary>
public sealed record SessionSummary(int AnsweredCount, int CorrectCount, int XpEarned, IReadOnlyList<LevelChange> LevelChanges);

/// <summary>
/// Adds a session up (session-summary.ts). The level a word stood at before each answer is read
/// from the attempt, since mastery keeps only where a word stands now. A word is listed once
/// per answer that changed its level, in the order given.
/// </summary>
public static class SessionSummaries
{
    public static SessionSummary Of(IReadOnlyList<SummarisedAttempt> attempts) => new(
        attempts.Count,
        attempts.Count(attempt => attempt.IsCorrect),
        attempts.Sum(attempt => attempt.XpAwarded),
        [.. attempts.Where(attempt => attempt.LevelBefore != attempt.LevelAfter).Select(attempt => new LevelChange(attempt.English, attempt.LevelAfter))]);
}
