using LanguZe.Api.Modules.Auth.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.RateLimiting;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Auth.Features.SignIn;

internal static class SignInEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/auth/sign-in", HandleAsync)
            .AllowAnonymous()
            .WithRateLimit(5, TimeSpan.FromMinutes(15))
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

    private static async Task<Ok<MeResponse>> HandleAsync(Body<SignInRequest> body, HttpContext context, SignInHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(body.Value, context, cancellationToken));
}
