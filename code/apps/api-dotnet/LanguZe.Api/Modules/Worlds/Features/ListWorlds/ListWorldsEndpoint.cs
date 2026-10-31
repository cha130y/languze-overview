using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.ListWorlds;

/// <summary>The learner's worlds, newest first (FR-013).</summary>
internal static class ListWorldsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/worlds", HandleAsync);

    private static async Task<Ok<List<WorldSummaryResponse>>> HandleAsync(CurrentAccount current, ListWorldsHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(current.Id, cancellationToken));
}
