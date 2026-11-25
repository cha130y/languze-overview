using System.Diagnostics;
using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;

namespace LanguZe.Api.Modules.Ai.Contracts;

/// <summary>Which job a call does, and who answers it.</summary>
public sealed record AiCallDetails(AiPurpose Purpose, string Provider, string Model);

/// <summary>
/// Records what every AI call cost and how it went (ai-call-recorder.ts; AIR-007, NFR-017).
/// The row holds no learner, photo, prompt or message: only which job it was, which model
/// answered, how long it took, and the outcome.
/// </summary>
public sealed partial class AiCallRecorder(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AiCallRecorder> logger)
{
    /// <summary>
    /// Runs one provider call and writes its record either way. A failure to write it is
    /// logged rather than thrown: losing a cost record must never turn a good answer into a
    /// failed analysis. <paramref name="requestId"/> ties the call to the request that caused
    /// it (NFR-016), which a background run carries over from its request.
    /// </summary>
    public async Task<T> RecordAsync<T>(AiCallDetails details, string? requestId, Func<Task<AiAnswer<T>>> call)
    {
        var startedAt = clock.GetUtcNow();
        var started = Stopwatch.GetTimestamp();
        AiAnswer<T> answer;
        try
        {
            answer = await call();
        }
        catch (Exception error)
        {
            var (outcome, code) = OutcomeOf(error);
            await WriteAsync(details, requestId, startedAt, started, outcome, null, code);
            throw;
        }

        await WriteAsync(details, requestId, startedAt, started, AiOutcome.Succeeded, answer.Usage, null);
        return answer.Value;
    }

    /// <summary>
    /// The same record for a streamed call (AIR-007). A stream reports its cost only when it
    /// ends, so the record is written once the last event has gone past, and the events are
    /// passed through untouched, so nothing waits for it.
    /// </summary>
    public async IAsyncEnumerable<T> StreamAsync<T>(AiCallDetails details, string? requestId, IAsyncEnumerable<T> events, Func<T, AiUsage?> usageOf)
    {
        var startedAt = clock.GetUtcNow();
        var started = Stopwatch.GetTimestamp();
        AiUsage? usage = null;
        await using var enumerator = events.GetAsyncEnumerator();
        while (true)
        {
            // Only the provider's failures are recorded: the yield sits outside the try, as an
            // async iterator needs, and a consumer that stops early records nothing.
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
            }
            catch (Exception error)
            {
                var (outcome, code) = OutcomeOf(error);
                await WriteAsync(details, requestId, startedAt, started, outcome, null, code);
                throw;
            }

            usage = usageOf(enumerator.Current) ?? usage;
            yield return enumerator.Current;
        }

        await WriteAsync(details, requestId, startedAt, started, AiOutcome.Succeeded, usage, null);
    }

    private static (AiOutcome Outcome, string Code) OutcomeOf(Exception error) => error switch
    {
        AiProviderException { Failure: AiFailure.TimedOut } failure => (AiOutcome.TimedOut, failure.Code),
        AiProviderException { Failure: AiFailure.InvalidOutput } failure => (AiOutcome.InvalidOutput, failure.Code),
        AiProviderException failure => (AiOutcome.Failed, failure.Code),
        _ => (AiOutcome.Failed, error.GetType().Name),
    };

    private async Task WriteAsync(AiCallDetails details, string? requestId, DateTimeOffset startedAt, long started, AiOutcome outcome, AiUsage? usage, string? errorCode)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LanguZeDbContext>();
            db.AiCalls.Add(new AiCall
            {
                Purpose = details.Purpose,
                Provider = details.Provider,
                Model = details.Model,
                StartedAt = startedAt,
                LatencyMs = (int)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, MidpointRounding.AwayFromZero),
                InputTokens = usage?.InputTokens,
                OutputTokens = usage?.OutputTokens,
                Outcome = outcome,
                ErrorCode = errorCode,
                RequestId = requestId,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception error)
        {
            LogNotRecorded(details.Purpose, error.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the {Purpose} call: {Reason}")]
    private partial void LogNotRecorded(AiPurpose purpose, string reason);
}
