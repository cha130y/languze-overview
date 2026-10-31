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
            var (outcome, code) = error switch
            {
                AiProviderException { Failure: AiFailure.TimedOut } failure => (AiOutcome.TimedOut, failure.Code),
                AiProviderException { Failure: AiFailure.InvalidOutput } failure => (AiOutcome.InvalidOutput, failure.Code),
                AiProviderException failure => (AiOutcome.Failed, failure.Code),
                _ => (AiOutcome.Failed, error.GetType().Name),
            };
            await WriteAsync(details, requestId, startedAt, started, outcome, null, code);
            throw;
        }

        await WriteAsync(details, requestId, startedAt, started, AiOutcome.Succeeded, answer.Usage, null);
        return answer.Value;
    }

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
