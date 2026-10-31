using LanguZe.Api.Data.Entities;

namespace LanguZe.Api.Modules.Storage.Contracts;

/// <summary>A file on its way into photo storage.</summary>
public sealed record PhotoUpload(string Key, ReadOnlyMemory<byte> Body, string ContentType);

/// <summary>
/// Where prepared photos live (A3), as photo-storage.ts: what the rest of the API sees, so
/// the object store behind it, Cloudflare R2 in production and SeaweedFS locally, is one
/// adapter away. Photos are private: nothing is served from a public URL, and a browser
/// receives a link that works for a few minutes and only for one object (P4, NFR-008).
/// </summary>
public interface IPhotoStorage
{
    Task StoreAsync(PhotoUpload upload, CancellationToken cancellationToken);

    /// <summary>The bytes back, which the analysis needs to send the photo to an AI provider.</summary>
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Content when the object is gone already, since the cleanup task retries.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);

    Task<string> SignedLinkAsync(string key);
}

public static class StorageKeys
{
    /// <summary>
    /// The key of a new object. Random, and never built from the learner, the world, or the
    /// file name: a key that could be guessed from such data would let one learner ask for
    /// another's photo, and the key of a deleted account's photo would still describe it.
    /// </summary>
    public static string New(PhotoKind kind, string extension = "jpg") =>
        $"{kind.ToString().ToLowerInvariant()}/{Guid.NewGuid()}.{extension}";
}
