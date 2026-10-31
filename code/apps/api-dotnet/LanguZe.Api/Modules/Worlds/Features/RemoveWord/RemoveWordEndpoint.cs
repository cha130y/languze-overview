using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.RemoveWord;

/// <summary>Removes a word the analysis got wrong (FR-026, US-022).</summary>
internal static class RemoveWordEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/worlds/{worldId}/words/{occurrenceId}", HandleAsync)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

    private static async Task<NoContent> HandleAsync(string worldId, string occurrenceId, CurrentAccount current, RemoveWordHandler handler, CancellationToken cancellationToken)
    {
        // The route's IDs are checked in their order, as NestJS's pipes run.
        var world = WorldIds.Parse(worldId);
        await handler.HandleAsync(current.Id, world, WorldIds.Parse(occurrenceId), cancellationToken);
        return TypedResults.NoContent();
    }
}
