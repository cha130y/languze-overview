using LanguZe.Api.Data;
using LanguZe.Api.Modules.Storage.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Storage.Jobs;

/// <summary>
/// Removes photos from storage after the rows that referenced them are gone (NFR-009), as
/// photo-cleanup.service.ts does. The deletion of a world or an account writes a cleanup
/// record in the same transaction as the database change, so the record exists exactly when
/// an object still has to go; this task does the slow, failable part on its own, and a
/// failure only means the record is tried again, never that a photo is quietly kept (V10).
/// <para>
/// The sweep only reads the database while records may exist. Each writer calls
/// <see cref="Expect"/> once its transaction has committed, and the sweep stops looking when
/// none are left; sweeping every ten minutes regardless kept Neon's compute from ever
/// suspending (production deployment, section 10). A writer that forgets is still caught by
/// the six-hourly check, well inside V10's 24 hours.
/// </para>
/// </summary>
internal sealed partial class PhotoCleanup(IServiceScopeFactory scopes, IPhotoStorage storage, TimeProvider clock, ILogger<PhotoCleanup> logger)
    : BackgroundService, IPhotoCleanup
{
    /// <summary>How many cleanup records one sweep takes, so a backlog cannot stall the task.</summary>
    private const int BatchSize = 50;

    /// <summary>NestJS's <c>CronExpression.EVERY_10_MINUTES</c>: on the clock's ten minutes.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long to wait after a failed attempt, by attempt number. Deletion has to happen
    /// within 24 hours of the request (V10), so the waits stay short enough for many tries
    /// inside that window; anything beyond the list repeats the last one.
    /// </summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    ];

    /// <summary>Whether cleanup records may exist. True at startup: a previous process may have left some.</summary>
    private volatile bool _pending = true;

    public static TimeSpan RetryDelay(int attempts) => RetryDelays[Math.Clamp(attempts, 1, RetryDelays.Length) - 1];

    /// <summary>The first ten minutes of the clock after <paramref name="now"/>, as the cron expression fires.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % Every.Ticks) + Every.Ticks, TimeSpan.Zero);

    /// <summary>Whether <paramref name="run"/> is also the six-hourly check, <c>CronExpression.EVERY_6_HOURS</c>.</summary>
    public static bool IsRecheck(DateTimeOffset run) => run.UtcDateTime is { Hour: 0 or 6 or 12 or 18, Minute: 0 };

    public void Expect() => _pending = true;

    /// <summary>One scheduled run: a sweep while records may exist, nothing otherwise.</summary>
    public async Task RemoveDeletedPhotosAsync(CancellationToken cancellationToken)
    {
        if (!_pending)
        {
            return;
        }

        // Cleared before the sweep, so a record committed while it runs sets it again.
        _pending = false;
        try
        {
            await SweepAsync(cancellationToken);
            // A postponed record is not due yet, but it still needs a later sweep.
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<LanguZeDbContext>().PhotoDeletions.AnyAsync(cancellationToken))
            {
                _pending = true;
            }
        }
        catch
        {
            _pending = true;
            throw;
        }
    }

    /// <summary>Removes each due record's object, then the record; answers how many were removed.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LanguZeDbContext>();
        var due = await db.PhotoDeletions
            .Where(record => record.NextAttemptAt <= now)
            .OrderBy(record => record.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var record in due)
        {
            try
            {
                await storage.RemoveAsync(record.StorageKey, cancellationToken);
                db.PhotoDeletions.Remove(record);
                await db.SaveChangesAsync(cancellationToken);
                removed++;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                record.Attempts++;
                record.NextAttemptAt = now + RetryDelay(record.Attempts);
                // Long provider errors are cut to fit the column and to keep logs readable.
                record.LastError = error.Message.Length > 500 ? error.Message[..500] : error.Message;
                await db.SaveChangesAsync(cancellationToken);
                LogAttemptFailed(record.Attempts, error.Message);
            }
        }

        if (removed > 0)
        {
            LogRemoved(removed);
        }

        return removed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = NextRun(now);
            await Task.Delay(next - now, clock, stoppingToken);
            if (IsRecheck(next))
            {
                // The safety net for a record whose writer did not call Expect.
                Expect();
            }

            try
            {
                await RemoveDeletedPhotosAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // @nestjs/schedule logs a failed run and keeps the schedule.
                LogFailed(error);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} photo(s) from storage")]
    private partial void LogRemoved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo cleanup attempt {Attempt} failed: {Reason}")]
    private partial void LogAttemptFailed(int attempt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "The photo cleanup run failed")]
    private partial void LogFailed(Exception error);
}
