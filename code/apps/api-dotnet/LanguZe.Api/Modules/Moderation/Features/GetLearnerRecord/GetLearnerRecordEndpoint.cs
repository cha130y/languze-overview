using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Moderation.Features.GetLearnerRecord;

/// <summary>A learner's moderation record (FR-104, US-103).</summary>
internal static class GetLearnerRecordEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/v1/admin/learners/{learnerId}", HandleAsync).AdminOnly();

    private static async Task<Ok<LearnerRecordResponse>> HandleAsync(string learnerId, LearnerRecords records, CancellationToken cancellationToken)
    {
        var id = RouteIds.Parse(learnerId);
        return TypedResults.Ok(await records.RecordWhereAsync(user => user.Id == id, cancellationToken));
    }
}
