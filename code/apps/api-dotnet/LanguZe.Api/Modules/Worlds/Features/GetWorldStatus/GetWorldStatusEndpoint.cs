using LanguZe.Api.Data;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.GetWorldStatus;

/// <summary>Status alone, which the waiting page asks for repeatedly (FR-021, P3).</summary>
internal static class GetWorldStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/worlds/{worldId}/status", HandleAsync);

    private static async Task<Ok<WorldStatusResponse>> HandleAsync(string worldId, CurrentAccount current, WorldReader reader, CancellationToken cancellationToken)
    {
        var world = await reader.OwnAsync(current.Id, WorldIds.Parse(worldId), cancellationToken);
        return TypedResults.Ok(new WorldStatusResponse(WorldReader.StatusName(world.Status), WorldReader.FailureName(world.FailureReason)));
    }
}
