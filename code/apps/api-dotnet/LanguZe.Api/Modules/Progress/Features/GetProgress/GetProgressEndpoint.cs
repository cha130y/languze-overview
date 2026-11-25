using LanguZe.Api.Modules.Auth.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Progress.Features.GetProgress;

/// <summary>Total XP, words at each level, and each world's share (FR-061, US-060).</summary>
internal static class GetProgressEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/v1/progress", HandleAsync);

    private static async Task<Ok<ProgressResponse>> HandleAsync(CurrentAccount current, GetProgressHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(current.Id, cancellationToken));
}
