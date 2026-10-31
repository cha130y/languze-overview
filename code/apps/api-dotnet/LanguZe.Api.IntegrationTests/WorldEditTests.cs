using System.Net;
using System.Net.Http.Headers;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Shared;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// Deleting a world, removing a word and retrying, where only the database or a failing
/// provider shows what happened, as worlds.e2e-spec.ts and analysis.e2e-spec.ts check through
/// Prisma and the fake provider.
/// </summary>
public sealed class WorldEditTests
{
    // FR-015, NFR-009: the photos are left for the cleanup task in the same transaction.
    [Fact]
    public async Task Deleting_a_world_leaves_its_photos_for_cleanup()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "delete-photos@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        await using var before = api.Database.Context();
        var keys = await before.StoredPhotos.Select(photo => photo.StorageKey).OrderBy(key => key).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(browser, $"/v1/worlds/{world}")).StatusCode);

        await using var db = api.Database.Context();
        Assert.False(await db.Worlds.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.StoredPhotos.AnyAsync(TestContext.Current.CancellationToken));
        var cleanup = await db.PhotoDeletions.OrderBy(record => record.StorageKey).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(keys, cleanup.Select(record => record.StorageKey));
        Assert.All(cleanup, record => Assert.Equal(PhotoDeletionReason.WorldDeleted, record.Reason));
    }

    // US-014 criterion 3: a word nothing else has goes with the world, with its mastery and mistakes.
    [Fact]
    public async Task Deleting_a_world_takes_its_words_with_their_mastery_and_mistakes()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "delete-words@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        await using (var db = api.Database.Context())
        {
            var word = await db.WordOccurrences.Where(occurrence => occurrence.WorldId == world).Select(occurrence => occurrence.VocabularyWord).FirstAsync(TestContext.Current.CancellationToken);
            db.WordMastery.Add(new WordMastery { VocabularyWordId = word.Id, LearnerId = word.LearnerId, Level = MasteryLevel.Learning, LastPractisedAt = api.Clock.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO attempts (id, learner_id, vocabulary_word_id, answer_text, is_correct, level_after) VALUES (gen_random_uuid(), {word.LearnerId}, {word.Id}, 'zzz', false, 'LEARNING')",
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(browser, $"/v1/worlds/{world}")).StatusCode);

        await using var after = api.Database.Context();
        Assert.False(await after.VocabularyWords.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await after.WordMastery.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await after.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM attempts").SingleAsync(TestContext.Current.CancellationToken));
    }

    // US-014 criterion 2: a word another world still has stays.
    [Fact]
    public async Task Deleting_a_world_keeps_the_words_another_world_has()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "delete-keeps@example.com");
        var first = await Learners.AnalysedWorldAsync(api, browser);
        await Learners.AnalysedWorldAsync(api, browser);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(browser, $"/v1/worlds/{first}")).StatusCode);

        await using var db = api.Database.Context();
        Assert.Equal(6, await db.VocabularyWords.CountAsync(TestContext.Current.CancellationToken));
    }

    // FR-027: the word goes with its last occurrence, and stays while another world has it.
    [Fact]
    public async Task Removing_a_word_deletes_it_only_with_its_last_occurrence()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "remove-word@example.com");
        var first = await Learners.AnalysedWorldAsync(api, browser);
        var second = await Learners.AnalysedWorldAsync(api, browser);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(browser, $"/v1/worlds/{first}/words/{await SofaInAsync(api, first)}")).StatusCode);
        await using (var db = api.Database.Context())
        {
            Assert.Equal(1, await db.VocabularyWords.CountAsync(word => word.English == "sofa", TestContext.Current.CancellationToken));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(browser, $"/v1/worlds/{second}/words/{await SofaInAsync(api, second)}")).StatusCode);
        await using (var db = api.Database.Context())
        {
            Assert.False(await db.VocabularyWords.AnyAsync(word => word.English == "sofa", TestContext.Current.CancellationToken));
        }
    }

    // US-021: a failed world keeps its photo, and a retry that goes well makes it READY.
    [Fact]
    public async Task A_failed_world_keeps_its_photo_and_a_retry_that_goes_well_makes_it_ready()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "retry@example.com");
        var fake = api.Service<FakeAiProvider>();
        fake.Behaviour = "PROVIDER_ERROR";
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var failed = await ApiHost.BodyAsync(await browser.GetAsync(new Uri($"/v1/worlds/{world}", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("FAILED", failed.GetProperty("status").GetString());
        Assert.Contains("X-Amz-Signature", failed.GetProperty("photoUrl").GetString(), StringComparison.Ordinal);
        fake.Behaviour = "ALLOWED";

        using var response = await browser.PostAsync(new Uri($"/v1/worlds/{world}/retry", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await api.Service<AnalysisRunner>().SettledAsync(world);
        var ready = await ApiHost.BodyAsync(await browser.GetAsync(new Uri($"/v1/worlds/{world}", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("READY", ready.GetProperty("status").GetString());
        Assert.Equal(6, ready.GetProperty("words").GetArrayLength());
    }

    // FR-025: a retry is a new analysis, so it waits for tomorrow like any other.
    [Fact]
    public async Task A_retry_once_today_is_used_up_is_refused_and_leaves_the_world_failed()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "retry-limit@example.com");
        api.Service<FakeAiProvider>().Behaviour = "PROVIDER_ERROR";
        var world = await Learners.AnalysedWorldAsync(api, browser);
        await using (var db = api.Database.Context())
        {
            var learner = await db.Users.Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken);
            db.Analyses.AddRange(Enumerable.Range(0, 10).Select(_ => new Analysis { LearnerId = learner, Status = AnalysisStatus.Succeeded }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await browser.PostAsync(new Uri($"/v1/worlds/{world}/retry", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal("DAILY_ANALYSIS_LIMIT", await ApiHost.ErrorAsync(response, HttpStatusCode.TooManyRequests));
        await using var after = api.Database.Context();
        Assert.Equal(WorldStatus.Failed, await after.Worlds.Select(row => row.Status).SingleAsync(TestContext.Current.CancellationToken));
    }

    // A blocked photo is gone, so its world can only be deleted.
    [Fact]
    public async Task A_world_whose_photo_was_removed_cannot_be_retried()
    {
        await using var api = await ApiHost.StartAsync();
        using var browser = await Learners.VerifiedAsync(api, "retry-blocked@example.com");
        Guid world;
        await using (var db = api.Database.Context())
        {
            var learner = await db.Users.Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken);
            var blocked = new World { LearnerId = learner, Name = "ห้อง", Status = WorldStatus.Failed, FailureReason = AnalysisFailure.Blocked };
            db.Worlds.Add(blocked);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            world = blocked.Id;
        }

        using var response = await browser.PostAsync(new Uri($"/v1/worlds/{world}/retry", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = (await ApiHost.BodyAsync(response)).GetProperty("error");
        Assert.Equal("RETRY_NOT_AVAILABLE", error.GetProperty("code").GetString());
        Assert.Equal("This photo was removed, so the world can only be deleted.", error.GetProperty("message").GetString());
    }

    private static async Task<Guid> SofaInAsync(ApiHost api, Guid world)
    {
        await using var db = api.Database.Context();
        return await db.WordOccurrences.Where(occurrence => occurrence.WorldId == world && occurrence.VocabularyWord.English == "sofa")
            .Select(occurrence => occurrence.Id).SingleAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient browser, string path) =>
        browser.DeleteAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
}
