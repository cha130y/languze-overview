using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.RateLimiting;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Learning.Features.StartSession;

/// <summary>Starts a game of a world, or a review across them (FR-030, FR-050).</summary>
internal static class StartSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/sessions", HandleAsync)
            // Starting a game costs a write and a read of every word, so it is not free.
            .WithRateLimit(20, TimeSpan.FromMinutes(1))
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

    private static async Task<Created<SessionResponse>> HandleAsync(Body<StartSessionRequest> body, CurrentAccount current, StartSessionHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await handler.HandleAsync(current.Id, body.Value, cancellationToken));
}
