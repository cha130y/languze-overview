using System.Globalization;
using LanguZe.Api.Modules.Worlds.Jobs;

namespace LanguZe.Api.Tests.Worlds;

/// <summary>stale-analysis.service.spec.ts: the timings of the check; what it releases, and when it looks, is tested on a database in LanguZe.Api.IntegrationTests.</summary>
public sealed class StaleAnalysisScheduleTests
{
    // V16: five minutes is lost, not slow, and the watch lasts one minute run past it.
    [Fact]
    public void An_analysis_has_five_minutes_and_is_watched_for_one_run_after()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), StaleAnalysis.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(6), StaleAnalysis.Watch);
    }

    // CronExpression.EVERY_MINUTE runs on the clock's minutes.
    [Theory]
    [InlineData("2026-10-22T10:00:30.000Z", "2026-10-22T10:01:00.000Z")]
    [InlineData("2026-10-22T10:01:00.000Z", "2026-10-22T10:02:00.000Z")]
    [InlineData("2026-10-22T23:59:59.999Z", "2026-10-23T00:00:00.000Z")]
    public void Runs_on_the_clocks_minutes(string now, string next) =>
        Assert.Equal(Instant(next), StaleAnalysis.NextRun(Instant(now)));

    private static DateTimeOffset Instant(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}
