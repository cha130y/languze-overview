using System.Text.RegularExpressions;
using LanguZe.Api.Platform.Errors;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>NestJS's <c>ParseUUIDPipe</c> on a route's ID: versions 1 to 8 with the RFC variant, or the nil and max UUIDs.</summary>
internal static partial class WorldIds
{
    public static Guid Parse(string value) =>
        Uuid().IsMatch(value)
            ? Guid.Parse(value)
            : throw new AppException(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest, "Validation failed (uuid is expected)");

    [GeneratedRegex("^(?:[0-9A-F]{8}-[0-9A-F]{4}-[1-8][0-9A-F]{3}-[89AB][0-9A-F]{3}-[0-9A-F]{12}|00000000-0000-0000-0000-000000000000|FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF)\\z", RegexOptions.IgnoreCase)]
    private static partial Regex Uuid();
}
