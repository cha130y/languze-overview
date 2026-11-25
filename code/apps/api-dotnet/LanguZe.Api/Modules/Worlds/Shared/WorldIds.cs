using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>NestJS's <c>ParseUUIDPipe</c> on a world's ID (<see cref="RouteIds"/>).</summary>
internal static class WorldIds
{
    public static Guid Parse(string value) => RouteIds.Parse(value);
}
