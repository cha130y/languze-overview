using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Jobs;

/// <summary>
/// Releases analyses that never finished (V16), as stale-analysis.service.ts does. Background
/// work lives in the API process (P2), so a restart in the middle of an analysis leaves a world
/// saying "analysing" for ever and an analysis counted against the learner's day. This runs at
/// startup, which is exactly when that has just happened, and every minute afterwards for a run
/// that hung.
/// <para>
/// The minute check only reads the database while an analysis may still be running. Analyses
/// start in this process alone (A2), which says so through <see cref="Expect"/>, so outside that
/// window there is nothing to release; reading anyway, every minute, kept Neon's compute from
/// ever suspending (production deployment, section 10).
/// </para>
/// </summary>
internal sealed partial class StaleAnalysis(IServiceScopeFactory scopes, TimeProvider clock, ILogger<StaleAnalysis> logger) : BackgroundService
{
    /// <summary>An analysis that has run this long is lost, not slow (V16, FR-021).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>How long the minute check keeps looking after an analysis starts: the timeout, then one more run.</summary>
    public static readonly TimeSpan Watch = Timeout + TimeSpan.FromMinutes(1);

    /// <summary>NestJS's <c>CronExpression.EVERY_MINUTE</c>: on the clock's minutes.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    private readonly Lock _watchLock = new();

    /// <summary>Until when an analysis may still be running.</summary>
    private DateTimeOffset _watchUntil = DateTimeOffset.MinValue;

    /// <summary>Called when an analysis starts, so the minute check looks until it must have ended.</summary>
    public void Expect()
    {
        var until = clock.GetUtcNow() + Watch;
        lock (_watchLock)
        {
            if (until > _watchUntil)
            {
                _watchUntil = until;
            }
        }
    }

    /// <summary>One scheduled run: a release while an analysis may still be running, nothing otherwise.</summary>
    public async Task ReleaseLostAnalysesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset until;
        lock (_watchLock)
        {
            until = _watchUntil;
        }

        var now = clock.GetUtcNow();
        if (now > until)
        {
            return;
        }

        await ReleaseAsync(now, cancellationToken);
    }

    /// <summary>Fails every analysis still in progress after the timeout, with its world; answers how many.</summary>
    public async Task<int> ReleaseAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var staleBefore = now - Timeout;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LanguZeDbContext>();
        var stale = await db.Analyses
            .Where(analysis => analysis.Status == AnalysisStatus.InProgress && analysis.StartedAt < staleBefore)
            .Select(analysis => new { analysis.Id, analysis.WorldId })
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        var ids = stale.Select(analysis => analysis.Id).ToList();
        var worldIds = stale.Where(analysis => analysis.WorldId is not null).Select(analysis => analysis.WorldId!.Value).ToList();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Released, because the learner's photo may have been perfectly good (FR-025).
            await db.Analyses.Where(analysis => ids.Contains(analysis.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(analysis => analysis.Status, AnalysisStatus.Failed)
                        .SetProperty(analysis => analysis.FailureReason, AnalysisFailure.TimedOut)
                        .SetProperty(analysis => analysis.FinishedAt, now),
                    cancellationToken);

            // Only a world still waiting: one that finished another way is left alone.
            await db.Worlds.Where(world => worldIds.Contains(world.Id) && world.Status == WorldStatus.Analyzing)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(world => world.Status, WorldStatus.Failed)
                        .SetProperty(world => world.FailureReason, AnalysisFailure.TimedOut)
                        .SetProperty(world => world.UpdatedAt, now),
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        LogReleased(stale.Count);
        return stale.Count;
    }

    /// <summary>
    /// A restart is the usual reason an analysis is lost, so this releases first. One the
    /// previous process started in its last five minutes is not stale yet, so the minute check
    /// watches for it too.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Expect();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run here rather than in StartAsync, so a database that is not there yet is logged
        // like a failed minute run instead of stopping the API from starting.
        try
        {
            await ReleaseAsync(clock.GetUtcNow(), stoppingToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            LogFailed(error);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            await Task.Delay(NextRun(now) - now, clock, stoppingToken);
            try
            {
                await ReleaseLostAnalysesAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // @nestjs/schedule logs a failed run and keeps the schedule.
                LogFailed(error);
            }
        }
    }

    /// <summary>The next whole minute of the clock after <paramref name="now"/>, as the cron expression <c>0 * * * * *</c> fires.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % Every.Ticks) + Every.Ticks, TimeSpan.Zero);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} analysis(es) that never finished")]
    private partial void LogReleased(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The release of analyses that never finished failed")]
    private partial void LogFailed(Exception error);
}
