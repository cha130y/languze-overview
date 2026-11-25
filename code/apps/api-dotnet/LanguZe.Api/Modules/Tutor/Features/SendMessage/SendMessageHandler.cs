using LanguZe.Api.Platform.Records;
using System.Text;
using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Tutor.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Text;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Tutor.Features.SendMessage;

/// <summary>A message that has passed every check and already holds one of today's.</summary>
/// <param name="Day">The day the message was taken from, so the right one is given back (V1).</param>
internal sealed record TurnInProgress(Guid LearnerId, string Content, DateOnly Day, int MessagesLeft);

/// <summary>
/// Answering a tutor message (tutor-reply.service.ts; FR-070 to FR-075, NFR-003). Split in two
/// on purpose: <see cref="BeginAsync"/> does everything that may refuse the message while the
/// response can still carry a JSON error, and only then does the endpoint open the stream,
/// after which a failure can only be an <c>error</c> event.
/// </summary>
internal sealed class SendMessageHandler(
    LanguZeDbContext db,
    IAiProvider ai,
    AiCallRecorder recorder,
    TutorTools tools,
    TutorAllowance allowance,
    IAiVerification verification,
    IAiAccess aiAccess,
    TrafficLog traffic,
    TimeProvider clock)
{
    /// <summary>The longest message a learner may send, in UTF-16 units as JavaScript counts (FR-071, V7).</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>
    /// How much of the conversation the tutor is reminded of: enough to follow a thread, few
    /// enough that a long conversation cannot grow the cost of every message without limit
    /// (AIR-006, ADR-0004).
    /// </summary>
    public const int RememberedMessages = 20;

    /// <summary>Checks the message and takes one of today's (FR-006, FR-071, V7); everything that can refuse belongs here.</summary>
    public async Task<TurnInProgress> BeginAsync(Guid learnerId, string content, CancellationToken cancellationToken)
    {
        if (content.Length > MaxMessageLength)
        {
            throw new AppException(
                ErrorCodes.MessageTooLong,
                StatusCodes.Status400BadRequest,
                $"A message can be at most {MaxMessageLength} characters.",
                new Dictionary<string, object?> { ["limit"] = MaxMessageLength });
        }

        // The tutor is an AI feature, so it needs a verified account whose AI features are not paused (FR-006, FR-096).
        await aiAccess.RequireAsync(
            learnerId,
            await verification.IsVerifiedForAiAsync(learnerId, cancellationToken),
            "Verify your email address to ask the tutor.",
            cancellationToken);

        var (day, messagesLeft) = await allowance.ReserveAsync(learnerId, clock.GetUtcNow(), cancellationToken);
        await traffic.RecordAsync(TrafficEventKind.TutorMessage, learnerId, null, cancellationToken);
        return new TurnInProgress(learnerId, content, day, messagesLeft);
    }

    /// <summary>
    /// The reply, piece by piece, saved once it is whole. Nothing is written to the conversation
    /// until the provider has finished: a half-answer is not a reply, and a learner who saw one
    /// would have paid a message for it. Any failure gives the message back instead (FR-071, S6).
    /// It runs to the end whether or not the learner is still there, so the answer is waiting
    /// next time.
    /// </summary>
    public async Task<DoneEvent> ReplyAsync(TurnInProgress turn, Func<string, Task> onDelta, string requestId)
    {
        try
        {
            var reply = new StringBuilder();
            var events = recorder.StreamAsync(
                new AiCallDetails(AiPurpose.Tutor, ai.Name, ai.ModelFor(AiPurpose.Tutor)),
                requestId,
                ai.TutorReplyAsync(new TutorTurn(await RememberedAsync(turn.LearnerId), turn.Content, tools.ForLearner(turn.LearnerId)), CancellationToken.None),
                // The cost arrives with the last event of the turn (AIR-007).
                tutorEvent => tutorEvent is TutorDone done ? done.Usage : null);
            await foreach (var tutorEvent in events)
            {
                if (tutorEvent is TutorDelta delta)
                {
                    reply.Append(delta.Text);
                    await onDelta(delta.Text);
                }
            }

            // A provider that streamed nothing has not answered. Saving it would put an empty
            // message in the conversation and charge the learner for it.
            if (JsText.Trim(reply.ToString()).Length == 0)
            {
                throw new AiProviderException(AiFailure.InvalidOutput, "The tutor gave no reply to save.");
            }

            var (messageId, replyId) = await RememberAsync(turn.LearnerId, turn.Content, reply.ToString());
            return new DoneEvent(messageId, replyId, turn.MessagesLeft);
        }
        catch
        {
            await allowance.ReleaseAsync(turn.LearnerId, turn.Day);
            throw;
        }
    }

    /// <summary>The code the browser is told a failed reply by (API design, section 3.6).</summary>
    public static string ErrorCodeOf(Exception error) => error switch
    {
        AppException app => app.Code,
        AiProviderException => ErrorCodes.AiProviderUnavailable,
        _ => ErrorCodes.InternalError,
    };

    /// <summary>What the tutor is reminded of, oldest first, as the provider wants it.</summary>
    private async Task<IReadOnlyList<TutorExchange>> RememberedAsync(Guid learnerId)
    {
        var newestFirst = await db.TutorMessages.Where(message => message.LearnerId == learnerId)
            .OrderByDescending(message => message.CreatedAt)
            .Take(RememberedMessages)
            .Select(message => new TutorExchange(message.Role, message.Content))
            .ToListAsync();
        newestFirst.Reverse();
        return newestFirst;
    }

    /// <summary>
    /// Saves the message and the reply together, or neither. The times are set here, the reply
    /// a millisecond after the message, so the conversation has an order to read them back in.
    /// </summary>
    private async Task<(Guid MessageId, Guid ReplyId)> RememberAsync(Guid learnerId, string content, string reply)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        var message = new TutorMessage { LearnerId = learnerId, Role = TutorRole.Learner, Content = content, CreatedAt = now };
        var answer = new TutorMessage { LearnerId = learnerId, Role = TutorRole.Tutor, Content = reply, CreatedAt = now.AddMilliseconds(1) };
        db.TutorMessages.AddRange(message, answer);
        await db.SaveChangesAsync();
        return (message.Id, answer.Id);
    }
}
