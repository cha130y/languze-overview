using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.GetWorld;

/// <summary>One world with its photo and words (FR-014).</summary>
internal static class GetWorldEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/worlds/{worldId}", HandleAsync)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

    private static async Task<Ok<WorldDetailResponse>> HandleAsync(string worldId, CurrentAccount current, WorldReader reader, CancellationToken cancellationToken) =>
        TypedResults.Ok(await reader.DetailOfAsync(await reader.OwnAsync(current.Id, WorldIds.Parse(worldId), cancellationToken)));
}
