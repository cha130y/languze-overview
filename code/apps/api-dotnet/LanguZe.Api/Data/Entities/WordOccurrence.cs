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

    /// <summary>The Thai meaning the analysis gave the word in this photo, shown with it in this world (FR-043).</summary>
    public string ThaiMeaning { get; set; } = string.Empty;

    public double BoxX { get; set; }

    public double BoxY { get; set; }

    public double BoxWidth { get; set; }

    public double BoxHeight { get; set; }

    // The arrow the Identify question points at the object (#73): the spot on it and where the
    // arrow starts, both relative to the prepared photo (0–1). All empty when there is no arrow,
    // as for worlds made before it, and the question shows the box alone.

    public double? PointX { get; set; }

    public double? PointY { get; set; }

    public double? TailX { get; set; }

    public double? TailY { get; set; }

    public PointSource? PointSource { get; set; }

    public string ExampleSentence { get; set; } = string.Empty;

    public CefrLevel CefrLevel { get; set; }

    public List<string>? AcceptedVariants { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
