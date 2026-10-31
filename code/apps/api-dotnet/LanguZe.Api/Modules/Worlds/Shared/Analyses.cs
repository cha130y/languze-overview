using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Worlds.Jobs;
using LanguZe.Api.Platform.Config;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>
/// The daily analyses and how one starts (analysis.service.ts; FR-020, FR-025): an analysis
/// is taken before anything is stored, attached to its world once that exists, and given
/// back if the world could not be made.
/// </summary>
internal sealed class Analyses(LanguZeDbContext db, UsageLimits limits, AnalysisRunner runner, StaleAnalysis stale, TimeProvider clock)
{
    /// <summary>Statuses that have used one of the learner's analyses for today (FR-020, U2).</summary>
    public static readonly AnalysisStatus[] Counted = [AnalysisStatus.InProgress, AnalysisStatus.Succeeded, AnalysisStatus.Blocked];

    /// <summary>
    /// Counts today's analyses and takes one, both inside a transaction holding a lock on
    /// this learner. Without the lock two uploads sent at once could each see nine used and
    /// both take the tenth (U2).
    /// </summary>
    public async Task<Guid> TakeAsync(Guid learnerId, Guid? worldId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({learnerId.ToString()}, 0))", cancellationToken);

        var today = BangkokDay.StartOf(clock.GetUtcNow());
        var used = await db.Analyses.CountAsync(
            analysis => analysis.LearnerId == learnerId && analysis.StartedAt >= today && Counted.Contains(analysis.Status),
            cancellationToken);
        if (used >= limits.DailyAnalyses)
        {
            throw new AppException(
                ErrorCodes.DailyAnalysisLimit,
                StatusCodes.Status429TooManyRequests,
                $"You have used all {limits.DailyAnalyses} photo analyses for today.",
                new Dictionary<string, object?> { ["limit"] = limits.DailyAnalyses });
        }

        var analysis = new Analysis { LearnerId = learnerId, WorldId = worldId };
        db.Analyses.Add(analysis);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Every analysis starts here, so this is where the timeout check learns of it (V16).
        stale.Expect();
        return analysis.Id;
    }

    /// <summary>Attaches a taken analysis to its world and starts the work, which the learner does not wait for (P2).</summary>
    public async Task BeginAsync(Guid analysisId, Guid worldId, string storageKey, string requestId, CancellationToken cancellationToken)
    {
        await db.Analyses.Where(analysis => analysis.Id == analysisId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(analysis => analysis.WorldId, worldId), cancellationToken);
        runner.Start(analysisId, worldId, storageKey, requestId);
    }

    /// <summary>Gives back an analysis whose world could not be made: released rather than counted, since nothing was analysed (FR-025).</summary>
    public async Task ReleaseAsync(Guid analysisId)
    {
        var now = clock.GetUtcNow();
        await db.Analyses.Where(analysis => analysis.Id == analysisId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(analysis => analysis.Status, AnalysisStatus.Failed)
                .SetProperty(analysis => analysis.FailureReason, AnalysisFailure.ProviderError)
                .SetProperty(analysis => analysis.FinishedAt, now));
    }
}
