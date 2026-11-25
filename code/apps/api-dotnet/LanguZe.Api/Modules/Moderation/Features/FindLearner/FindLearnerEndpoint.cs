using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Moderation.Features.FindLearner;

/// <summary>
/// One learner, by an exact address or an account ID (FR-108, US-106). It answers with the record
/// itself: the admin searched in order to see it, and it holds nothing the search did not
/// already entitle them to.
/// </summary>
internal static class FindLearnerEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/admin/learners", HandleAsync)
            .WithMetadata(new QueryMetadata(typeof(FindLearnerQuery)))
            .AdminOnly()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

    private static async Task<Ok<LearnerRecordResponse>> HandleAsync(HttpContext context, FindLearnerHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(RequestQuery.Check<FindLearnerQuery>(context), cancellationToken));
}
