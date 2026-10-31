using System.Text;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>A world's form as it was read: its text fields, and the photo if one came.</summary>
internal sealed record WorldForm(IReadOnlyDictionary<string, JsValue> Fields, byte[]? Photo);

/// <summary>
/// Reads the form that creates a world as NestJS's <c>FileInterceptor('photo')</c> does,
/// with multer 2.5 and busboy 1.6 under it, and answers each limit in their words.
/// <list type="bullet">
/// <item>The form is one field and one file, and nothing more is read: a second field is
/// "Too many fields", a second file "Too many files", decided as the part's headers arrive;
/// a third part neither of those stops the form when it ends, "Too many parts".</item>
/// <item>A field value of more than 1,024 bytes is "Field value too long - name"; a file of
/// more than 10 MB is PHOTO_TOO_LARGE (FR-011). Each is counted in bytes as it arrives,
/// never read further.</item>
/// <item>A file under any name but <c>photo</c> is "Unexpected file field - picture", as
/// NestJS's exception filter words it since multer 2's wording outran NestJS's mapping.</item>
/// <item>A part is a file when it names a file or is <c>application/octet-stream</c>.</item>
/// </list>
/// The file is kept in memory, never on disk: it is small by definition, and a temporary
/// file would be one more copy of a learner's photo to remove. A body that is not a form is
/// read as JSON, as Express's parser reads it before multer, which then finds no photo.
/// </summary>
internal static class PhotoForm
{
    /// <summary>The largest upload accepted, before preparation shrinks it (FR-011).</summary>
    public const int MaxPhotoBytes = 10 * 1024 * 1024;

    private const string PhotoField = "photo";
    private const int MaxFieldBytes = 1024;
    private const int MaxParts = 2;

    public static async Task<WorldForm> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldForm(await RequestBody.ReadFieldsAsync(request, cancellationToken), null);
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            throw Refused("Multipart: Boundary not found");
        }

        var fields = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        byte[]? photo = null;
        int files = 0, fieldCount = 0, parts = 0;
        var reader = new MultipartReader(boundary, request.Body) { BodyLengthLimit = null };
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
                {
                    // busboy skips a part that is not form data, and counts it as a part.
                    await section.Body.CopyToAsync(Stream.Null, cancellationToken);
                }
                else
                {
                    var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                    var isFile = disposition.FileNameStar.HasValue || disposition.FileName.HasValue
                        || section.ContentType?.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase) == true;
                    if (isFile)
                    {
                        if (files == 1)
                        {
                            throw Refused("Too many files");
                        }

                        files++;
                        if (string.IsNullOrEmpty(name))
                        {
                            throw Refused("Field name missing");
                        }

                        if (name != PhotoField)
                        {
                            throw Refused($"Unexpected file field - {name}");
                        }

                        photo = await ReadAtMostAsync(section.Body, MaxPhotoBytes, cancellationToken)
                            ?? throw new AppException(ErrorCodes.PhotoTooLarge, StatusCodes.Status413PayloadTooLarge, "This photo is larger than 10 MB.");
                    }
                    else
                    {
                        if (fieldCount == 1)
                        {
                            throw Refused("Too many fields");
                        }

                        fieldCount++;
                        var value = await ReadAtMostAsync(section.Body, MaxFieldBytes, cancellationToken);
                        if (string.IsNullOrEmpty(name))
                        {
                            throw Refused("Field name missing");
                        }

                        fields[name] = value is null
                            ? throw Refused($"Field value too long - {name}")
                            : JsValue.Of(Encoding.UTF8.GetString(value));
                    }
                }

                if (++parts > MaxParts)
                {
                    throw Refused("Too many parts");
                }
            }
        }
        catch (IOException)
        {
            throw Refused("Multipart: Unexpected end of form");
        }

        return new WorldForm(fields, photo);
    }

    /// <summary>The bytes of a part, or nothing when it holds more than <paramref name="limit"/>.</summary>
    private static async Task<byte[]?> ReadAtMostAsync(Stream body, int limit, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > limit)
            {
                return null;
            }

            bytes.Write(buffer, 0, read);
        }

        return bytes.ToArray();
    }

    private static AppException Refused(string message) =>
        new(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest, message);
}
