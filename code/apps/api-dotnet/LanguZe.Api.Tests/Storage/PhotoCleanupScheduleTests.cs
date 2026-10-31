using System.Globalization;
using LanguZe.Api.Modules.Storage.Jobs;

namespace LanguZe.Api.Tests.Storage;

/// <summary>photo-cleanup.service.spec.ts: the timings of the cleanup; what it removes is tested against storage and a database in LanguZe.Api.IntegrationTests.</summary>
public sealed class PhotoCleanupScheduleTests
{
    // Deletion has to happen within 24 hours (V10), so the waits stay short enough for many tries.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(9, 60)]
    public void Each_failure_waits_longer_then_an_hour(int attempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), PhotoCleanup.RetryDelay(attempts));

    // CronExpression.EVERY_10_MINUTES runs on the clock's ten minutes.
    [Theory]
    [InlineData("2026-09-21T10:02:30.000Z", "2026-09-21T10:10:00.000Z")]
    [InlineData("2026-09-21T10:10:00.000Z", "2026-09-21T10:20:00.000Z")]
    [InlineData("2026-09-21T23:55:00.000Z", "2026-09-22T00:00:00.000Z")]
    public void Runs_on_the_clocks_ten_minutes(string now, string next) =>
        Assert.Equal(Instant(next), PhotoCleanup.NextRun(Instant(now)));

    // CronExpression.EVERY_6_HOURS: the safety net for a writer that forgot to say.
    [Theory]
    [InlineData("2026-09-21T00:00:00.000Z", true)]
    [InlineData("2026-09-21T06:00:00.000Z", true)]
    [InlineData("2026-09-21T18:00:00.000Z", true)]
    [InlineData("2026-09-21T06:10:00.000Z", false)]
    [InlineData("2026-09-21T07:00:00.000Z", false)]
    public void Looks_again_every_six_hours(string run, bool recheck) =>
        Assert.Equal(recheck, PhotoCleanup.IsRecheck(Instant(run)));

    private static DateTimeOffset Instant(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}
