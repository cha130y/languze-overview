using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Learning.Features.GetCurrentSession;

/// <summary>
/// The session still open for a world, or the open review (FR-036). 204 means there is none,
/// which the page reads as "offer a new game" rather than as an error.
/// </summary>
internal static class GetCurrentSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/sessions/current", HandleAsync)
            .WithMetadata(new QueryMetadata(typeof(CurrentSessionQuery)));

    private static async Task<Results<Ok<SessionResponse>, NoContent>> HandleAsync(CurrentAccount current, HttpContext context, GetCurrentSessionHandler handler, CancellationToken cancellationToken)
    {
        var session = await handler.HandleAsync(current.Id, RequestQuery.Check<CurrentSessionQuery>(context), cancellationToken);
        return session is null ? TypedResults.NoContent() : TypedResults.Ok(session);
    }
}
