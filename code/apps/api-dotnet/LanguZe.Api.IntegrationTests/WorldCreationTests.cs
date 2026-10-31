using System.Net;
using System.Net.Http.Headers;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Shared;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// Creating a world where only the database shows what happened, as worlds.e2e-spec.ts and
/// analysis.e2e-spec.ts check through Prisma: the limits on worlds and on today's analyses,
/// what is stored and what an analysis records, and each way the fake provider can fail.
/// </summary>
public sealed class WorldCreationTests
{
    // FR-017, V18: the stored photo is the prepared one, not what was uploaded, and only it reaches the provider.
    [Fact]
    public async Task The_prepared_photo_and_its_thumbnail_are_stored_and_only_they_are_analysed()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "stored@example.com");
        var upload = Learners.Photo(3000, 2000);

        var world = await Learners.AnalysedWorldAsync(api, browser, upload);

        await using var db = api.Database.Context();
        var photos = await db.StoredPhotos.OrderBy(photo => photo.Kind).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([(PhotoKind.Prepared, 2048, 1365), (PhotoKind.Thumbnail, 320, 213)], photos.Select(photo => (photo.Kind, photo.Width, photo.Height)));
        Assert.All(photos, photo => Assert.Equal("image/jpeg", photo.ContentType));
        var sent = api.Service<FakeAiProvider>().Calls;
        Assert.Equal([AiPurpose.SafetyCheck, AiPurpose.Extraction], sent.Select(call => call.Purpose));
        Assert.All(sent, call => Assert.Equal(photos[0].ByteSize, call.Bytes));
        Assert.Equal(WorldStatus.Ready, (await db.Worlds.SingleAsync(row => row.Id == world, TestContext.Current.CancellationToken)).Status);
    }

    // AIR-007: each call is recorded with its cost, tied to the request that caused it, and nothing about the learner.
    [Fact]
    public async Task Each_ai_call_is_recorded_with_the_request_that_caused_it()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "recorded@example.com");

        using var response = await Learners.PostWorldAsync(browser, Learners.Photo(800, 600));
        await api.Service<AnalysisRunner>().SettledAsync(Guid.Parse((await ApiHost.BodyAsync(response)).GetProperty("id").GetString()!));

        await using var db = api.Database.Context();
        var calls = await db.AiCalls.OrderBy(call => call.StartedAt).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [(AiPurpose.SafetyCheck, "fake-safety_check", 780, 12), (AiPurpose.Extraction, "fake-extraction", 820, 240)],
            calls.Select(call => (call.Purpose, call.Model, call.InputTokens ?? 0, call.OutputTokens ?? 0)));
        Assert.All(calls, call =>
        {
            Assert.Equal("fake", call.Provider);
            Assert.Equal(AiOutcome.Succeeded, call.Outcome);
            Assert.Equal(response.Headers.GetValues("X-Request-Id").Single(), call.RequestId);
        });
    }

    // FR-024, FR-025: each way an analysis can fail ends the world FAILED and gives the analysis back.
    [Theory]
    [InlineData("PROVIDER_ERROR", AnalysisFailure.ProviderError, AiOutcome.Failed)]
    [InlineData("TIMED_OUT", AnalysisFailure.TimedOut, AiOutcome.TimedOut)]
    [InlineData("TOO_FEW_WORDS", AnalysisFailure.TooFewWords, AiOutcome.Succeeded)]
    [InlineData("INVALID_OUTPUT", AnalysisFailure.InvalidOutput, AiOutcome.Succeeded)]
    public async Task A_failed_analysis_fails_the_world_and_is_not_counted(string behaviour, AnalysisFailure reason, AiOutcome lastCall)
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "failed@example.com");
        api.Service<FakeAiProvider>().Behaviour = behaviour;

        var world = await Learners.AnalysedWorldAsync(api, browser, Learners.Photo(800, 600));

        await using var db = api.Database.Context();
        var stored = await db.Worlds.Include(row => row.Occurrences).SingleAsync(row => row.Id == world, TestContext.Current.CancellationToken);
        Assert.Equal((WorldStatus.Failed, reason), (stored.Status, stored.FailureReason));
        Assert.Empty(stored.Occurrences);
        var analysis = await db.Analyses.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((AnalysisStatus.Failed, reason), (analysis.Status, analysis.FailureReason));
        Assert.NotNull(analysis.FinishedAt);
        Assert.Equal(lastCall, (await db.AiCalls.OrderByDescending(call => call.StartedAt).FirstAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    // A word is a learner's once, however many photos it is in (FR-022).
    [Fact]
    public async Task A_word_found_in_two_worlds_is_one_word_with_two_occurrences()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "two-worlds@example.com");

        await Learners.AnalysedWorldAsync(api, browser, Learners.Photo(800, 600));
        await Learners.AnalysedWorldAsync(api, browser, Learners.Photo(800, 600));

        await using var db = api.Database.Context();
        Assert.Equal(6, await db.VocabularyWords.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(12, await db.WordOccurrences.CountAsync(TestContext.Current.CancellationToken));
        var sofa = await db.WordOccurrences.Where(occurrence => occurrence.VocabularyWord.English == "sofa").ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, sofa.Select(occurrence => occurrence.WorldId).Distinct().Count());
        Assert.Single(sofa.Select(occurrence => occurrence.VocabularyWordId).Distinct());
        Assert.All(sofa, occurrence => Assert.Equal(["couch"], occurrence.AcceptedVariants));
    }

    // FR-012: worlds.e2e-spec.ts "refuses the twenty-first world".
    [Fact]
    public async Task The_twenty_first_world_is_WORLD_LIMIT_REACHED()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "limit@example.com");
        await using (var db = api.Database.Context())
        {
            var learner = await db.Users.Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken);
            db.Worlds.AddRange(Enumerable.Range(1, 20).Select(index => new World { LearnerId = learner, Name = $"โลกที่ {index}", Status = WorldStatus.Ready }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await Learners.PostWorldAsync(browser, Learners.Photo(800, 600));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = (await ApiHost.BodyAsync(response)).GetProperty("error");
        Assert.Equal("WORLD_LIMIT_REACHED", error.GetProperty("code").GetString());
        Assert.Equal("A learner can have 20 worlds. Delete one to make room.", error.GetProperty("message").GetString());
        Assert.Equal("""{"limit":20}""", error.GetProperty("details").GetRawText());
    }

    // FR-020: the eleventh analysis of a Bangkok day is refused before anything is stored.
    [Fact]
    public async Task The_eleventh_analysis_of_the_day_is_refused_with_nothing_left_behind()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "daily@example.com");
        await using (var db = api.Database.Context())
        {
            var learner = await db.Users.Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken);
            db.Analyses.AddRange(Enumerable.Range(0, 10).Select(_ => new Analysis { LearnerId = learner, Status = AnalysisStatus.Succeeded }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await Learners.PostWorldAsync(browser, Learners.Photo(800, 600));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var error = (await ApiHost.BodyAsync(response)).GetProperty("error");
        Assert.Equal("DAILY_ANALYSIS_LIMIT", error.GetProperty("code").GetString());
        Assert.Equal("You have used all 10 photo analyses for today.", error.GetProperty("message").GetString());
        Assert.Equal("""{"limit":10}""", error.GetProperty("details").GetRawText());
        await using var after = api.Database.Context();
        Assert.False(await after.Worlds.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await after.StoredPhotos.AnyAsync(TestContext.Current.CancellationToken));
    }

    // A failed analysis was given back, so it leaves today's ten untouched (FR-025).
    [Fact]
    public async Task Failed_analyses_do_not_count_toward_the_day()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "released@example.com");
        await using (var db = api.Database.Context())
        {
            var learner = await db.Users.Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken);
            db.Analyses.AddRange(Enumerable.Range(0, 9).Select(_ => new Analysis { LearnerId = learner, Status = AnalysisStatus.Succeeded }));
            db.Analyses.AddRange(Enumerable.Range(0, 5).Select(_ => new Analysis { LearnerId = learner, Status = AnalysisStatus.Failed, FailureReason = AnalysisFailure.ProviderError }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await Learners.PostWorldAsync(browser, Learners.Photo(800, 600));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
