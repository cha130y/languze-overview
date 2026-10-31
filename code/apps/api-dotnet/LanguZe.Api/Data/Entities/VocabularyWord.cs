namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A word a learner has, once however many photos it appears in (data model,
/// <c>vocabulary_words</c>; FR-022). Two spellings of one Thai meaning are one word,
/// which <see cref="ThaiMeaningKey"/> decides.
/// </summary>
public sealed class VocabularyWord
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public string English { get; set; } = string.Empty;

    public string ThaiMeaning { get; set; } = string.Empty;

    public string ThaiMeaningKey { get; set; } = string.Empty;

    public WordMastery? Mastery { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
