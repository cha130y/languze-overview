namespace LanguZe.Api.Data.Entities;

/// <summary>
/// How far a learner has got with one word (data model, <c>word_mastery</c>; FR-040).
/// A word with no row has never been answered, which is new.
/// </summary>
public sealed class WordMastery
{
    public Guid VocabularyWordId { get; set; }

    public Guid LearnerId { get; set; }

    public MasteryLevel Level { get; set; }

    public short Streak { get; set; }

    /// <summary>The Bangkok day the word became familiar (V1).</summary>
    public DateOnly? FamiliarOn { get; set; }

    public DateTimeOffset LastPractisedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
