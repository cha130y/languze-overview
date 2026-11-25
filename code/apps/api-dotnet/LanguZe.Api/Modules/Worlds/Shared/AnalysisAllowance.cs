using LanguZe.Api.Data;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Platform.Config;
using LanguZe.Api.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>Counts today's analyses as taking one does (analysis.service.ts, <c>usage</c>; FR-020, FR-080).</summary>
internal sealed class AnalysisAllowance(LanguZeDbContext db, UsageLimits limits) : IAnalysisUsage
{
    public async Task<AnalysisUsage> TodayAsync(Guid learnerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = BangkokDay.StartOf(now);
        var used = await db.Analyses.CountAsync(
            analysis => analysis.LearnerId == learnerId && analysis.StartedAt >= today && Analyses.Counted.Contains(analysis.Status),
            cancellationToken);
        return new AnalysisUsage(used, limits.DailyAnalyses, Math.Max(0, limits.DailyAnalyses - used));
    }
}
