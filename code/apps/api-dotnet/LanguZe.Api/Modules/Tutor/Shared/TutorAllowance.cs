using LanguZe.Api.Data;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Platform.Config;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Tutor.Shared;

/// <summary>
/// The day's tutor messages (tutor.service.ts; FR-071): one row per learner per Bangkok day,
/// so a count from another day is never read (V1).
/// </summary>
internal sealed class TutorAllowance(LanguZeDbContext db, UsageLimits limits, TimeProvider clock) : ITutorUsage
{
    /// <summary>
    /// The Bangkok day as a date, which is what the table keeps (V1). A <c>date</c> column holds
    /// a day rather than an instant, so it is the day itself, not a moment a time zone could move.
    /// </summary>
    public static DateOnly DayOf(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(BangkokDay.Offset).DateTime);

    public async Task<TutorUsage> TodayAsync(Guid learnerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = DayOf(now);
        var used = await db.TutorDailyUsage.Where(usage => usage.LearnerId == learnerId && usage.Day == day)
            .Select(usage => (int?)usage.MessagesUsed)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        return new TutorUsage(used, limits.DailyTutorMessages, Math.Max(0, limits.DailyTutorMessages - used));
    }

    /// <summary>
    /// Takes one of today's messages, or refuses (FR-071). One statement, because the count is a
    /// column of one row per learner per day: the insert creates that row, and on a day that
    /// already has one the update raises the count only while it is still below the limit. Two
    /// messages sent at the same moment serialise on the row itself, so neither can take the
    /// last one twice. No row comes back when the guard refuses, which is what "none left" means.
    /// </summary>
    public async Task<(DateOnly Day, int MessagesLeft)> ReserveAsync(Guid learnerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = DayOf(now);
        var taken = await db.Database.SqlQuery<short>($"""
            INSERT INTO tutor_daily_usage (learner_id, day, messages_used, updated_at)
            VALUES ({learnerId}, {day}, 1, now())
            ON CONFLICT (learner_id, day) DO UPDATE
              SET messages_used = tutor_daily_usage.messages_used + 1,
                  updated_at = now()
              WHERE tutor_daily_usage.messages_used < {limits.DailyTutorMessages}
            RETURNING messages_used AS "Value"
            """).ToListAsync(cancellationToken);

        if (taken.Count == 0)
        {
            throw new AppException(
                ErrorCodes.DailyTutorLimit,
                StatusCodes.Status429TooManyRequests,
                $"You have used all {limits.DailyTutorMessages} tutor messages for today.",
                new Dictionary<string, object?> { ["limit"] = limits.DailyTutorMessages });
        }

        return (day, Math.Max(0, limits.DailyTutorMessages - taken[0]));
    }

    /// <summary>
    /// Gives a reserved message back, because nothing was answered (FR-071, S6). The day comes
    /// from the reservation rather than from now, so a reply that runs past Bangkok midnight
    /// gives back the message it actually took. A count already at nothing is left alone.
    /// </summary>
    public Task ReleaseAsync(Guid learnerId, DateOnly day)
    {
        var now = clock.GetUtcNow();
        return db.TutorDailyUsage.Where(usage => usage.LearnerId == learnerId && usage.Day == day && usage.MessagesUsed > 0)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(usage => usage.MessagesUsed, usage => (short)(usage.MessagesUsed - 1))
                .SetProperty(usage => usage.UpdatedAt, now));
    }
}
