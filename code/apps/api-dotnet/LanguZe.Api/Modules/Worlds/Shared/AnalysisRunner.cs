using System.Collections.Concurrent;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>Carries why an analysis failed, so every ending is handled in one place.</summary>
internal sealed class AnalysisFailedException(AnalysisFailure reason) : Exception($"The analysis failed: {reason}")
{
    public AnalysisFailure Reason { get; } = reason;
}

/// <summary>
/// Runs analyses after the answer has been sent, because a learner should not wait for an
/// AI provider (analysis.service.ts, <c>runInBackground</c>; P2, FR-021). Nobody is waiting
/// for an answer any more, so a failure is written down rather than thrown: the learner sees
/// the world's status. Each run has a scope of its own, since the request's is gone.
/// </summary>
internal sealed partial class AnalysisRunner(IServiceScopeFactory scopes, ILogger<AnalysisRunner> logger)
{
    /// <summary>Runs in flight by world, so tests can wait for one instead of polling.</summary>
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public void Start(Guid analysisId, Guid worldId, string storageKey, string requestId)
    {
        var run = Task.Run(() => RunAsync(analysisId, worldId, storageKey, requestId));
        _running[worldId] = run;
        _ = run.ContinueWith(_ => _running.TryRemove(new KeyValuePair<Guid, Task>(worldId, run)), TaskScheduler.Default);
    }

    /// <summary>Waits for a world's run, if one is in flight.</summary>
    public Task SettledAsync(Guid worldId) => _running.TryGetValue(worldId, out var run) ? run : Task.CompletedTask;

    /// <summary>
    /// What to record for an error. A provider's own words are kept, a timeout being a
    /// timeout, and anything else is a provider error, which is released (FR-025).
    /// </summary>
    public static AnalysisFailure ReasonFor(Exception error) => error switch
    {
        AnalysisFailedException failed => failed.Reason,
        AiProviderException { Failure: AiFailure.TimedOut } => AnalysisFailure.TimedOut,
        AiProviderException { Failure: AiFailure.InvalidOutput } => AnalysisFailure.InvalidOutput,
        _ => AnalysisFailure.ProviderError,
    };

    private async Task RunAsync(Guid analysisId, Guid worldId, string storageKey, string requestId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<AnalysisWork>();
        try
        {
            await work.RunAsync(analysisId, worldId, storageKey, requestId);
        }
        catch (Exception error)
        {
            // Anything unexpected, storage unreachable or a bug, is LanguZe's fault and is
            // released like a provider error (FR-025).
            if (error is not AnalysisFailedException)
            {
                LogEndedBadly(analysisId, error);
            }

            try
            {
                await work.FailedAsync(analysisId, worldId, ReasonFor(error));
            }
            catch (Exception failure)
            {
                LogNotMarkedFailed(analysisId, failure);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis {AnalysisId} ended badly")]
    private partial void LogEndedBadly(Guid analysisId, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis {AnalysisId} could not be marked failed")]
    private partial void LogNotMarkedFailed(Guid analysisId, Exception error);
}
