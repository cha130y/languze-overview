namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A place a learner photographed and the words found in it (data model, <c>worlds</c>;
/// FR-010–FR-016). Its photos are kept apart, so a blocked photo can go while the world
/// stays to explain why.
/// </summary>
public sealed class World
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public WorldStatus Status { get; set; }

    public AnalysisFailure? FailureReason { get; set; }

    public Guid? PhotoId { get; set; }

    public StoredPhoto? Photo { get; set; }

    public Guid? ThumbnailId { get; set; }

    public StoredPhoto? Thumbnail { get; set; }

    public List<WordOccurrence> Occurrences { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
