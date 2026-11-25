using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Users.Features.DeleteAccount;

/// <summary>Deletes the account and everything belonging to it (FR-007, US-009).</summary>
internal static class DeleteAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/me", HandleAsync)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

    private static async Task<NoContent> HandleAsync(Body<DeleteAccountRequest> body, CurrentAccount current, HttpContext context, DeleteAccountHandler handler, CancellationToken cancellationToken)
    {
        _ = body.Value;
        await handler.HandleAsync(current.Id, context, cancellationToken);
        return TypedResults.NoContent();
    }
}
