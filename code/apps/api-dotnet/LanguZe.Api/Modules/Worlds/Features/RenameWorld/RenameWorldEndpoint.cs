using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.RenameWorld;

/// <summary>
/// Renames a world (FR-016). The body is read after the route's ID, as NestJS runs
/// ParseUUIDPipe before ValidationPipe, which a <see cref="Body{T}"/> parameter would not:
/// it is bound before the endpoint runs.
/// </summary>
internal static class RenameWorldEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/v1/worlds/{worldId}", HandleAsync)
            .WithMetadata(new JsonBodyMetadata(typeof(RenameWorldRequest)));

    private static async Task<Ok<WorldDetailResponse>> HandleAsync(string worldId, CurrentAccount current, HttpContext context, RenameWorldHandler handler, CancellationToken cancellationToken)
    {
        var id = WorldIds.Parse(worldId);
        var request = await RequestBody.ReadAsync<RenameWorldRequest>(context);
        return TypedResults.Ok(await handler.HandleAsync(current.Id, id, request, cancellationToken));
    }
}
