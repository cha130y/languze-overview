using LanguZe.Api.Modules.Auth.Domain;

namespace LanguZe.Api.Tests.Auth;

/// <summary>Only someone who is 20 on every day of this year may sign up, on any day of it (V20).</summary>
public sealed class AgeGateTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 31)]
    public void Turning_21_this_year_is_old_enough_and_turning_20_is_not_from_its_first_day_to_its_last(int month, int day)
    {
        var now = new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero);

        Assert.True(AgeGate.IsOldEnough(2005, now));
        Assert.False(AgeGate.IsOldEnough(2006, now));
    }
}
