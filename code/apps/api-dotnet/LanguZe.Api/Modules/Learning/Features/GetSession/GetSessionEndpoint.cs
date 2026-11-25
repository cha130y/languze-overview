using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Learning.Features.GetSession;

/// <summary>One session of the learner's, whatever state it is in (FR-036).</summary>
internal static class GetSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/sessions/{sessionId}", HandleAsync)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

    private static async Task<Ok<SessionResponse>> HandleAsync(string sessionId, CurrentAccount current, GetSessionHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(current.Id, RouteIds.Parse(sessionId), cancellationToken));
}
