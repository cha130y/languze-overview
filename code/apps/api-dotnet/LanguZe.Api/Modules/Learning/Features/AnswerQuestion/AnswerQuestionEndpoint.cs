using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Learning.Features.AnswerQuestion;

/// <summary>
/// One answer (FR-032 to FR-034). The route's IDs are read before the body, the session's first,
/// as NestJS runs ParseUUIDPipe on each parameter before ValidationPipe reads the body.
/// </summary>
internal static class AnswerQuestionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/sessions/{sessionId}/questions/{questionId}/answer", HandleAsync)
            .WithMetadata(new JsonBodyMetadata(typeof(AnswerRequest)))
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

    private static async Task<Ok<AnswerResultResponse>> HandleAsync(string sessionId, string questionId, CurrentAccount current, HttpContext context, AnswerQuestionHandler handler, CancellationToken cancellationToken)
    {
        var session = RouteIds.Parse(sessionId);
        var question = RouteIds.Parse(questionId);
        var request = await RequestBody.ReadAsync<AnswerRequest>(context);
        return TypedResults.Ok(await handler.HandleAsync(current.Id, session, question, request, cancellationToken));
    }
}
