using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.RateLimiting;

namespace LanguZe.Api.Modules.Worlds.Features.CreateWorld;

internal static class CreateWorldEndpoint
{
    /// <summary>
    /// Answers 202, while NestJS documents a 201 with the world: the document is copied as it
    /// is, so the answer is an untyped result and the 201 is declared.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/worlds", HandleAsync)
            // Photo upload is one of the expensive routes (API design, section 5).
            .WithRateLimit(5, TimeSpan.FromMinutes(1))
            .WithMetadata(new FormBodyMetadata([new FormField("name", MinLength: 1, MaxLength: 50), new FormField("photo", IsFile: true)]))
            .Produces<WorldDetailResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);

    private static async Task<IResult> HandleAsync(CurrentAccount current, HttpContext context, CreateWorldHandler handler, CancellationToken cancellationToken) =>
        Results.Json(await handler.HandleAsync(current.Id, context, cancellationToken), statusCode: StatusCodes.Status202Accepted);
}
