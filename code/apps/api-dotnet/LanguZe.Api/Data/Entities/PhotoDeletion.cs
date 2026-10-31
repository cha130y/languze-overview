namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A photo file waiting to be removed from storage (data model, <c>photo_deletions</c>;
/// NFR-009, V10). It names the file by its key alone, so it outlives the rows that named
/// it, and the cleanup task retries it until the file is gone.
/// </summary>
public sealed class PhotoDeletion
{
    public Guid Id { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public PhotoDeletionReason Reason { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
