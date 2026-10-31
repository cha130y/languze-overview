using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.DeleteWorld;

internal static class DeleteWorldEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/worlds/{worldId}", HandleAsync);

    private static async Task<NoContent> HandleAsync(string worldId, CurrentAccount current, DeleteWorldHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(current.Id, WorldIds.Parse(worldId), cancellationToken);
        return TypedResults.NoContent();
    }
}
