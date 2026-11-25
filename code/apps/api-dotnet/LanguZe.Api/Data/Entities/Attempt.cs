namespace LanguZe.Api.Data.Entities;

/// <summary>
/// One answer (data model, <c>attempts</c>; FR-034). An incorrect one is also the learner's
/// mistake record (FR-042), which is why it keeps what they typed.
/// </summary>
public sealed class Attempt
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid VocabularyWordId { get; set; }

    /// <summary>Unique, so one question can never hold two attempts (FR-034, S5).</summary>
    public Guid? QuestionId { get; set; }

    public Guid? SessionId { get; set; }

    public Guid? OccurrenceId { get; set; }

    /// <summary>What the learner typed, up to 100 characters; empty for "I don't know" (V19).</summary>
    public string? AnswerText { get; set; }

    public bool IsDontKnow { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>10 for a correct answer, 0 otherwise (FR-060).</summary>
    public short XpAwarded { get; set; }

    /// <summary>Where the word stood before this answer; empty when it was new (SRS 4.1).</summary>
    public MasteryLevel? LevelBefore { get; set; }

    /// <summary>And where it stands after, which a summary names (FR-035).</summary>
    public MasteryLevel LevelAfter { get; set; }

    public DateTimeOffset AnsweredAt { get; set; }
}
