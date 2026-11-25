namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A word a learner has, once however many photos it appears in (data model,
/// <c>vocabulary_words</c>; FR-043): one English word is one word, and one mastery, since the
/// games ask for the English. Each photo keeps its own Thai meaning on its occurrence.
/// </summary>
public sealed class VocabularyWord
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public string English { get; set; } = string.Empty;

    /// <summary>The meaning of its first occurrence, for the places that show no photo (FR-043, SRS 2.4).</summary>
    public string ThaiMeaning { get; set; } = string.Empty;

    public WordMastery? Mastery { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
