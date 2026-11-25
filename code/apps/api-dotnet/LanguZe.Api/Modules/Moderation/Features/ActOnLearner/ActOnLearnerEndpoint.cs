using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Moderation.Features.ActOnLearner;

/// <summary>
/// Acts on a learner's case and records it (FR-105, FR-106, US-104). It answers with the audit
/// entry the action wrote, the one thing every action has in common: a deleted account has no
/// record left to show. The route's ID is read before the body, as ParseUUIDPipe runs first.
/// </summary>
internal static class ActOnLearnerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/admin/learners/{learnerId}/actions", HandleAsync)
            .WithMetadata(new JsonBodyMetadata(typeof(ActRequest)))
            .AdminOnly()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

    private static async Task<Created<AuditEntryResponse>> HandleAsync(string learnerId, CurrentAccount current, HttpContext context, ActOnLearnerHandler handler, CancellationToken cancellationToken)
    {
        var learner = RouteIds.Parse(learnerId);
        var request = await RequestBody.ReadAsync<ActRequest>(context);
        return TypedResults.Created((string?)null, await handler.HandleAsync(current.Id, learner, request, cancellationToken));
    }
}
