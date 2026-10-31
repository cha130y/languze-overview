namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A word where it appears in one world's photo, with its box and the sentence that
/// teaches it there (data model, <c>word_occurrences</c>; FR-022, AIR-003).
/// </summary>
public sealed class WordOccurrence
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid VocabularyWordId { get; set; }

    public VocabularyWord VocabularyWord { get; set; } = null!;

    public double BoxX { get; set; }

    public double BoxY { get; set; }

    public double BoxWidth { get; set; }

    public double BoxHeight { get; set; }

    public string ExampleSentence { get; set; } = string.Empty;

    public CefrLevel CefrLevel { get; set; }

    public List<string>? AcceptedVariants { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
