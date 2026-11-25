using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Http;
using LanguZe.Api.Platform.RateLimiting;
using LanguZe.Api.Platform.Validation;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace LanguZe.Api.Modules.Tutor.Features.SendMessage;

/// <summary>
/// Sends a message and streams the reply back (FR-070, NFR-003, P6). Everything that can refuse
/// the message runs first, while the response can still carry an ordinary JSON error; once the
/// stream is open, the only way left to report a failure is an <c>error</c> event. A message
/// costs an AI call, so the route is rate limited as well as counted: the daily limit is the
/// real one (FR-071), this only stops a script spending it in a second.
/// </summary>
internal static partial class SendMessageEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/tutor/messages", HandleAsync)
            .WithMetadata(new JsonBodyMetadata(typeof(SendMessageRequest)))
            .WithRateLimit(10, TimeSpan.FromMinutes(1))
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, ServerSentEvents.ContentType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, ServerSentEvents.ContentType)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests, ServerSentEvents.ContentType);

    private static async Task HandleAsync(CurrentAccount current, HttpContext context, SendMessageHandler handler, IOptions<JsonOptions> json, ILoggerFactory loggers)
    {
        var request = await RequestBody.ReadAsync<SendMessageRequest>(context);
        var turn = await handler.BeginAsync(current.Id, request.Content, context.RequestAborted);

        var stream = new ServerSentEvents(context, json.Value.SerializerOptions);
        await stream.OpenAsync();
        try
        {
            var done = await handler.ReplyAsync(turn, text => stream.SendAsync("delta", new DeltaEvent(text)), context.TraceIdentifier);
            await stream.SendAsync("done", done);
        }
        catch (Exception error)
        {
            // The learner already has a 200 and perhaps part of a reply, so this is the only
            // place left to say so. The message has been given back by now, and nothing saved.
            LogReplyFailed(loggers.CreateLogger(typeof(SendMessageEndpoint)), error.Message);
            await stream.SendAsync("error", new ErrorEvent(SendMessageHandler.ErrorCodeOf(error)));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A tutor reply failed: {Reason}")]
    private static partial void LogReplyFailed(ILogger logger, string reason);
}
