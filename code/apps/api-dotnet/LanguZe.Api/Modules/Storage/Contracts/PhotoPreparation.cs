using LanguZe.Api.Platform.Errors;
using NetVips;

namespace LanguZe.Api.Modules.Storage.Contracts;

/// <summary>One image as it is stored.</summary>
public sealed record PreparedImage(byte[] Data, string ContentType, int Width, int Height, int ByteSize);

/// <summary>The photo and the thumbnail the world list shows.</summary>
public sealed record PreparedPhoto(PreparedImage Prepared, PreparedImage Thumbnail);

/// <summary>
/// Turns an uploaded file into what LanguZe stores (FR-017), as photo-preparation.service.ts
/// does with sharp, through the same libvips. Three things happen before anything is written
/// down: the orientation the camera recorded is applied to the pixels, every piece of
/// metadata is dropped, the place the photo was taken among it, and the image is scaled so
/// its longer side is at most 2,048 pixels (V18). Only the result is stored, shown, and sent
/// to an AI provider, so the original never leaves this class.
/// </summary>
public sealed class PhotoPreparation
{
    /// <summary>The longest side of a stored photo (V18).</summary>
    public const int MaxPhotoSide = 2048;

    /// <summary>The longest side of the copy the world list shows (FR-013).</summary>
    public const int ThumbnailSide = 320;

    /// <summary>Everything stored is JPEG, so one format is served, sent to AI, and measured.</summary>
    public const string PhotoContentType = "image/jpeg";

    /// <summary>Only these three, decided by the file's content rather than its name (FR-011): libvips's loaders for them.</summary>
    private static readonly string[] AcceptedLoaders = ["jpegload_buffer", "pngload_buffer", "webpload_buffer"];

    public PreparedPhoto Prepare(byte[] upload)
    {
        var loader = Describe(upload);
        if (!AcceptedLoaders.Contains(loader, StringComparer.Ordinal))
        {
            throw new AppException(ErrorCodes.PhotoTypeNotAllowed, StatusCodes.Status415UnsupportedMediaType, "Only JPEG, PNG, and WebP photos can be used.");
        }

        return new PreparedPhoto(Render(upload, MaxPhotoSide, 82), Render(upload, ThumbnailSide, 70));
    }

    /// <summary>The loader libvips reads the file with, from its header alone, as sharp's <c>metadata()</c> reads it.</summary>
    private static string Describe(byte[] upload)
    {
        try
        {
            if (Image.FindLoadBuffer(upload) is not null)
            {
                using var header = Image.NewFromBuffer(upload, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
                return (string)header.Get("vips-loader");
            }
        }
        catch (VipsException)
        {
            // Too damaged to read: no photo either.
        }

        throw new AppException(ErrorCodes.PhotoTypeNotAllowed, StatusCodes.Status415UnsupportedMediaType, "This file could not be read as a photo.");
    }

    /// <summary>
    /// <c>thumbnail</c> applies the EXIF orientation to the pixels and never scales up, as
    /// sharp's <c>rotate()</c> and <c>withoutEnlargement</c> do; nothing of the original's
    /// metadata is kept in the JPEG. The encoder settings are sharp's <c>mozjpeg: true</c>.
    /// </summary>
    private static PreparedImage Render(byte[] upload, int longestSide, int quality)
    {
        using var image = Image.ThumbnailBuffer(upload, longestSide, height: longestSide, size: Enums.Size.Down);
        var data = image.JpegsaveBuffer(
            q: quality,
            optimizeCoding: true,
            trellisQuant: true,
            overshootDeringing: true,
            optimizeScans: true,
            quantTable: 3,
            keep: Enums.ForeignKeep.None);
        return new PreparedImage(data, PhotoContentType, image.Width, image.Height, data.Length);
    }
}
