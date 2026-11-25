using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Tutor.Features.GetConversation;

/// <summary>The conversation, newest first, a page at a time (FR-070, US-070).</summary>
internal static class GetConversationEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/tutor/messages", HandleAsync)
            .WithMetadata(new QueryMetadata(typeof(ConversationQuery)));

    private static async Task<Ok<TutorConversationResponse>> HandleAsync(CurrentAccount current, HttpContext context, GetConversationHandler handler, CancellationToken cancellationToken)
    {
        var query = RequestQuery.Check<ConversationQuery>(context);
        return TypedResults.Ok(await handler.HandleAsync(current.Id, query.Before is null ? null : Guid.Parse(query.Before), cancellationToken));
    }
}
