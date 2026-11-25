using LanguZe.Api.Modules.Moderation.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Moderation.Features.GetReviewQueue;

/// <summary>Cases awaiting review, oldest first (FR-103, US-102).</summary>
internal static class GetReviewQueueEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/v1/admin/review-queue", HandleAsync).AdminOnly();

    private static async Task<Ok<ReviewQueueResponse>> HandleAsync(GetReviewQueueHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
