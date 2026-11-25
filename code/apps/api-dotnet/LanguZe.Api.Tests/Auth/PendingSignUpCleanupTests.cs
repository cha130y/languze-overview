using System.Globalization;
using LanguZe.Api.Modules.Auth.Jobs;

namespace LanguZe.Api.Tests.Auth;

/// <summary>pending-sign-up-cleanup.service.spec.ts: the timings of the sweep; what it removes is tested on a database in LanguZe.Api.IntegrationTests.</summary>
public sealed class PendingSignUpCleanupTests
{
    [Fact]
    public void A_sign_up_has_fifteen_minutes_and_is_watched_for_one_run_after()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), PendingSignUpCleanup.Lifetime);
        Assert.Equal(TimeSpan.FromMinutes(21), PendingSignUpCleanup.Watch);
    }

    // CronExpression.EVERY_5_MINUTES runs on the clock's five minutes, not five minutes after the API started.
    [Theory]
    [InlineData("2026-09-21T10:02:30.000Z", "2026-09-21T10:05:00.000Z")]
    [InlineData("2026-09-21T10:05:00.000Z", "2026-09-21T10:10:00.000Z")]
    [InlineData("2026-09-21T10:09:59.999Z", "2026-09-21T10:10:00.000Z")]
    [InlineData("2026-09-21T23:58:00.000Z", "2026-09-22T00:00:00.000Z")]
    public void Runs_on_the_clocks_five_minutes(string now, string next) =>
        Assert.Equal(Instant(next), PendingSignUpCleanup.NextRun(Instant(now)));

    private static DateTimeOffset Instant(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}
