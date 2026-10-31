namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A photo file in storage and who owns it (data model, <c>stored_photos</c>; FR-017). The
/// row goes with its owner's account; the file does not, so deleting an account queues a
/// <see cref="PhotoDeletion"/> for each of these first.
/// </summary>
public sealed class StoredPhoto
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public PhotoKind Kind { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public int ByteSize { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
