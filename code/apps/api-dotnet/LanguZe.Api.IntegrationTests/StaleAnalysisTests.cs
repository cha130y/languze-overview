using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Worlds.Jobs;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// Analyses that never finished (V16), as analysis.e2e-spec.ts and stale-analysis.service.spec.ts
/// check them: released after five minutes with their world, and looked for only while one may
/// still be running.
/// </summary>
public sealed class StaleAnalysisTests
{
    [Fact]
    public async Task An_analysis_older_than_five_minutes_is_released_with_its_world()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "lost@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var lost = await InProgressAsync(api, learner, world, api.Clock.GetUtcNow().AddMinutes(-6));

        Assert.True(await api.Service<StaleAnalysis>().ReleaseAsync(api.Clock.GetUtcNow(), TestContext.Current.CancellationToken) >= 1);

        await using var db = api.Database.Context();
        var analysis = await db.Analyses.SingleAsync(row => row.Id == lost, TestContext.Current.CancellationToken);
        Assert.Equal((AnalysisStatus.Failed, AnalysisFailure.TimedOut), (analysis.Status, analysis.FailureReason));
        var after = await db.Worlds.SingleAsync(row => row.Id == world, TestContext.Current.CancellationToken);
        Assert.Equal((WorldStatus.Failed, AnalysisFailure.TimedOut), (after.Status, after.FailureReason));
    }

    [Fact]
    public async Task A_young_analysis_and_a_world_that_finished_another_way_are_left_alone()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "young@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var young = await InProgressAsync(api, learner, world, api.Clock.GetUtcNow().AddMinutes(-4), keepWorld: true);
        var old = await InProgressAsync(api, learner, world, api.Clock.GetUtcNow().AddMinutes(-6), keepWorld: true);

        await api.Service<StaleAnalysis>().ReleaseAsync(api.Clock.GetUtcNow(), TestContext.Current.CancellationToken);

        await using var db = api.Database.Context();
        Assert.Equal(AnalysisStatus.InProgress, (await db.Analyses.SingleAsync(row => row.Id == young, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(AnalysisStatus.Failed, (await db.Analyses.SingleAsync(row => row.Id == old, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(WorldStatus.Ready, (await db.Worlds.SingleAsync(row => row.Id == world, TestContext.Current.CancellationToken)).Status);
    }

    // The minute check reads only from an analysis's start until it must have ended, so Neon can suspend.
    [Fact]
    public async Task The_minute_check_looks_only_while_an_analysis_may_be_running()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "watch@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var stale = api.Service<StaleAnalysis>();

        api.Clock.Advance(StaleAnalysis.Watch + TimeSpan.FromMilliseconds(1));
        var lost = await InProgressAsync(api, learner, world, api.Clock.GetUtcNow().AddMinutes(-6));
        await stale.ReleaseLostAnalysesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.InProgress, await StatusAsync(api, lost));

        // A new analysis starts the watch again, and the next run releases the lost one.
        await Learners.AnalysedWorldAsync(api, browser);
        await stale.ReleaseLostAnalysesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Failed, await StatusAsync(api, lost));
    }

    // A restart is when analyses are lost: one the last process started is watched for from the start.
    [Fact]
    public async Task The_minute_check_watches_from_the_start_for_an_analysis_the_last_process_left()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "left@example.com");
        var left = await InProgressAsync(api, learner, worldId: null, api.Clock.GetUtcNow().AddMinutes(-4));

        api.Clock.Advance(TimeSpan.FromMinutes(2));
        await api.Service<StaleAnalysis>().ReleaseLostAnalysesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Failed, await StatusAsync(api, left));
    }

    /// <summary>An analysis still in progress since <paramref name="startedAt"/>, its world back to waiting unless told otherwise.</summary>
    private static async Task<Guid> InProgressAsync(ApiHost api, Guid learner, Guid? worldId, DateTimeOffset startedAt, bool keepWorld = false)
    {
        await using var db = api.Database.Context();
        if (worldId is not null && !keepWorld)
        {
            await db.Worlds.Where(world => world.Id == worldId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(world => world.Status, WorldStatus.Analyzing).SetProperty(world => world.FailureReason, (AnalysisFailure?)null),
                    TestContext.Current.CancellationToken);
        }

        var analysis = new Analysis { LearnerId = learner, WorldId = worldId, Status = AnalysisStatus.InProgress, StartedAt = startedAt };
        db.Analyses.Add(analysis);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return analysis.Id;
    }

    private static async Task<AnalysisStatus> StatusAsync(ApiHost api, Guid analysis)
    {
        await using var db = api.Database.Context();
        return await db.Analyses.Where(row => row.Id == analysis).Select(row => row.Status).SingleAsync(TestContext.Current.CancellationToken);
    }
}
