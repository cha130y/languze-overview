using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Moderation.Domain;

namespace LanguZe.Api.Tests.Moderation;

/// <summary>What a blocked photo earns, which pause a learner is under, and what the account endpoint tells them about the AI features.</summary>
public sealed class SuspensionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_block_below_the_threshold_earns_nothing() =>
        Assert.Null(Blocked(2));

    // V15 level 1: the threshold reached for the first time.
    [Fact]
    public void The_threshold_the_first_time_pauses_ai_for_a_week()
    {
        Assert.Equal(3, SuspensionRules.BlocksBeforeSuspension);
        Assert.Equal(new SuspensionToRaise(PauseReason.RepeatedBlocks, Now.AddDays(7)), Blocked(3));
    }

    // V15 level 2: the threshold again, after a suspension.
    [Fact]
    public void The_threshold_after_an_earlier_suspension_waits_for_an_admin() =>
        Assert.Equal(new SuspensionToRaise(PauseReason.RepeatedAfterSuspension, null), Blocked(3, suspendedBefore: true));

    [Fact]
    public void More_than_the_threshold_counts_as_the_threshold() =>
        Assert.Equal(Blocked(3), Blocked(5));

    // FR-098: the count is about a pattern, and this is about one photo.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Suspected_illegal_material_suspends_at_once_until_an_admin_reviews(bool suspendedBefore) =>
        Assert.Equal(new SuspensionToRaise(PauseReason.SuspectedIllegalMaterial, null), Blocked(1, suspendedBefore, suspectedIllegalMaterial: true));

    [Fact]
    public void The_window_counts_back_seven_days_from_now() =>
        Assert.Equal(Now.AddDays(-7), SuspensionRules.BlockWindowFrom(Now));

    [Fact]
    public void Without_a_suspension_the_ai_features_open_once_the_learner_is_verified()
    {
        Assert.Equal(new AiAccess(true, null, null, false), SuspensionRules.Access(verifiedForAi: true, suspension: null));
        Assert.Equal(new AiAccess(false, AiAccessReason.NotVerified, null, false), SuspensionRules.Access(verifiedForAi: false, suspension: null));
    }

    [Fact]
    public void A_suspension_with_an_end_says_until_when()
    {
        var suspension = new SuspensionRecord(Now.AddDays(-1), Now.AddDays(6), LiftedAt: null);

        Assert.Equal(new AiAccess(false, AiAccessReason.AiSuspended, Now.AddDays(6), false), SuspensionRules.Access(verifiedForAi: true, suspension));
    }

    [Fact]
    public void A_suspension_without_an_end_awaits_review()
    {
        var suspension = new SuspensionRecord(Now.AddDays(-1), EndsAt: null, LiftedAt: null);

        Assert.Equal(new AiAccess(false, AiAccessReason.AiSuspended, null, true), SuspensionRules.Access(verifiedForAi: true, suspension));
    }

    // Told to verify their address, someone suspended would do it and still be refused.
    [Fact]
    public void A_suspension_is_reported_ahead_of_an_unverified_address()
    {
        var suspension = new SuspensionRecord(Now.AddDays(-1), EndsAt: null, LiftedAt: null);

        Assert.Equal(AiAccessReason.AiSuspended, SuspensionRules.Access(verifiedForAi: false, suspension).Reason);
    }

    [Fact]
    public void A_lifted_or_ended_suspension_is_no_longer_in_force()
    {
        var lifted = new SuspensionRecord(Now.AddDays(-3), EndsAt: null, LiftedAt: Now.AddDays(-1));
        var ended = new SuspensionRecord(Now.AddDays(-8), Now.AddDays(-1), LiftedAt: null);
        var endingNow = new SuspensionRecord(Now.AddDays(-7), Now, LiftedAt: null);

        Assert.Null(SuspensionRules.Current([lifted, ended, endingNow], Now));
    }

    [Fact]
    public void Of_the_suspensions_in_force_the_one_that_started_last_is_current()
    {
        var earlier = new SuspensionRecord(Now.AddDays(-5), Now.AddDays(2), LiftedAt: null);
        var later = new SuspensionRecord(Now.AddDays(-1), EndsAt: null, LiftedAt: null);

        Assert.Equal(later, SuspensionRules.Current([earlier, later], Now));
        Assert.Equal(later, SuspensionRules.Current([later, earlier], Now));
    }

    private static SuspensionToRaise? Blocked(int blocksInWindow, bool suspendedBefore = false, bool suspectedIllegalMaterial = false) =>
        SuspensionRules.For(new BlockOutcome(suspectedIllegalMaterial, blocksInWindow, suspendedBefore), Now);
}
