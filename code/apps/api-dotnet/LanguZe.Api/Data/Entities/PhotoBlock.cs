namespace LanguZe.Api.Data.Entities;

/// <summary>
/// That a learner's photo was blocked, and why, never the photo itself (data model,
/// <c>photo_blocks</c>; FR-095). Counted over seven days to decide a pause (V15).
/// </summary>
public sealed class PhotoBlock
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public BlockCategory Category { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
