using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Worlds.Features.RetryWorld;

/// <summary>Runs the analysis of the same photo again (FR-025, US-021).</summary>
internal static class RetryWorldEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/worlds/{worldId}/retry", HandleAsync)
            // An upload's cost again, so an upload's limit, counted on this route (API design, section 5).
            .WithRateLimit(5, TimeSpan.FromMinutes(1))
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

    private static async Task<Accepted> HandleAsync(string worldId, CurrentAccount current, HttpContext context, RetryWorldHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(current.Id, WorldIds.Parse(worldId), context.TraceIdentifier, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }
}
