using LanguZe.Api.Modules.Auth.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Tutor.Features.ClearConversation;

/// <summary>Throws the conversation away (FR-076, US-073).</summary>
internal static class ClearConversationEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app.MapDelete("/v1/tutor/messages", HandleAsync);

    private static async Task<NoContent> HandleAsync(CurrentAccount current, ClearConversationHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(current.Id, cancellationToken);
        return TypedResults.NoContent();
    }
}
