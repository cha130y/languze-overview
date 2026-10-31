using System.Globalization;
using System.Net;
using System.Text.Json;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Shared;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Json;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// A photo that breaks the rules, the pause repeated blocks earn, and the admins told about a
/// case that waits for them, as analysis.e2e-spec.ts checks through Prisma and the fake
/// provider (US-091, US-092, FR-107). None of it can be reached from outside: the contract run's
/// provider allows every photo.
/// </summary>
public sealed class PhotoBlockTests
{
    private const string AlertAddress = "alerts@example.com";

    // US-091, FR-092–FR-095: the photo goes, the block is recorded, and words are never asked for.
    [Fact]
    public async Task A_blocked_photo_is_removed_recorded_and_fails_its_world()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "blocked@example.com");
        var fake = api.Service<FakeAiProvider>();
        fake.Behaviour = "BLOCKED";

        var world = await Learners.AnalysedWorldAsync(api, browser);

        var read = await WorldAsync(browser, world);
        Assert.Equal("FAILED", read.GetProperty("status").GetString());
        Assert.Equal("BLOCKED", read.GetProperty("failureReason").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("photoUrl").ValueKind);
        Assert.Equal([AiPurpose.SafetyCheck], fake.Calls.Select(call => call.Purpose));

        await using var db = api.Database.Context();
        Assert.False(await db.StoredPhotos.AnyAsync(photo => photo.OwnerId == learner, TestContext.Current.CancellationToken));
        var block = await db.PhotoBlocks.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((learner, BlockCategory.Violence), (block.LearnerId, block.Category));
        var cleanup = await db.PhotoDeletions.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, cleanup.Count);
        Assert.All(cleanup, record => Assert.Equal(PhotoDeletionReason.PhotoBlocked, record.Reason));
        var analysis = await db.Analyses.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((AnalysisStatus.Blocked, AnalysisFailure.Blocked), (analysis.Status, analysis.FailureReason));
        Assert.NotNull(analysis.FinishedAt);
    }

    // FR-093: a blocked photo is the learner's doing, so it uses one of the ten.
    [Fact]
    public async Task A_blocked_photo_counts_toward_the_day()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "blocked-count@example.com");
        await using (var db = api.Database.Context())
        {
            db.Analyses.AddRange(Enumerable.Range(0, 9).Select(_ => new Analysis { LearnerId = learner, Status = AnalysisStatus.Succeeded }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await BlockOneAsync(api, browser);
        using var response = await Learners.PostWorldAsync(browser, Learners.Photo(800, 600));

        Assert.Equal("DAILY_ANALYSIS_LIMIT", await ApiHost.ErrorAsync(response, HttpStatusCode.TooManyRequests));
    }

    // FR-025: the photo is gone, so the world can only be deleted.
    [Fact]
    public async Task A_blocked_world_cannot_be_retried()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "blocked-retry@example.com");
        var world = await BlockOneAsync(api, browser);

        using var response = await browser.PostAsync(new Uri($"/v1/worlds/{world}/retry", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal("RETRY_NOT_AVAILABLE", await ApiHost.ErrorAsync(response, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Below_the_threshold_nothing_is_paused()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "below@example.com");
        await EarlierBlocksAsync(api, learner, 1);

        await BlockOneAsync(api, browser);

        Assert.Null(await SuspensionOfAsync(api, learner));
        Assert.True((await AiAccessAsync(browser)).GetProperty("available").GetBoolean());
    }

    // V15 level 1: the third within seven days, with no earlier pause.
    [Fact]
    public async Task The_third_block_in_seven_days_pauses_ai_for_a_week()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "third@example.com");
        await EarlierBlocksAsync(api, learner, 2);

        await BlockOneAsync(api, browser);

        var raised = await SuspensionOfAsync(api, learner);
        Assert.Equal(SuspensionReason.RepeatedBlocks, raised?.Reason);
        Assert.Equal(api.Clock.GetUtcNow().AddDays(7), raised?.EndsAt);
        var access = await AiAccessAsync(browser);
        Assert.False(access.GetProperty("available").GetBoolean());
        Assert.Equal("AI_SUSPENDED", access.GetProperty("reason").GetString());
        Assert.False(access.GetProperty("awaitingReview").GetBoolean());
        Assert.Equal(raised!.EndsAt!.Value.UtcDateTime.ToString(UtcInstantConverter.Format, CultureInfo.InvariantCulture), access.GetProperty("until").GetString());
    }

    // V15 level 2: a learner who has served a pause and come back to it.
    [Fact]
    public async Task After_an_earlier_pause_the_threshold_waits_for_an_admin()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "again@example.com");
        await ServedPauseAsync(api, learner);
        await EarlierBlocksAsync(api, learner, 2);

        await BlockOneAsync(api, browser);

        var raised = await SuspensionOfAsync(api, learner);
        Assert.Equal(SuspensionReason.RepeatedAfterSuspension, raised?.Reason);
        Assert.Null(raised?.EndsAt);
        var access = await AiAccessAsync(browser);
        Assert.Equal(JsonValueKind.Null, access.GetProperty("until").ValueKind);
        Assert.True(access.GetProperty("awaitingReview").GetBoolean());
    }

    // FR-098: one photo is enough, whatever the count.
    [Fact]
    public async Task Suspected_illegal_material_pauses_ai_at_once()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "illegal@example.com");
        api.Service<FakeAiProvider>().BlockCategory = BlockCategory.SuspectedIllegalMaterial;

        await BlockOneAsync(api, browser);

        var raised = await SuspensionOfAsync(api, learner);
        Assert.Equal(SuspensionReason.SuspectedIllegalMaterial, raised?.Reason);
        Assert.Null(raised?.EndsAt);
    }

    // The window is seven days, so old blocks are not held against a learner.
    [Fact]
    public async Task Blocks_older_than_the_window_are_not_counted()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "old-blocks@example.com");
        await EarlierBlocksAsync(api, learner, 5, daysAgo: 8);

        await BlockOneAsync(api, browser);

        Assert.Null(await SuspensionOfAsync(api, learner));
    }

    // FR-008: one learner's blocks are no reason to pause another.
    [Fact]
    public async Task One_learners_blocks_pause_nobody_else()
    {
        await using var api = await ApiHost.StartAsync();
        var (mine, me) = await Learners.VerifiedLearnerAsync(api, "mine@example.com");
        var (theirs, them) = await Learners.VerifiedLearnerAsync(api, "theirs@example.com");
        await EarlierBlocksAsync(api, them, 2);

        await BlockOneAsync(api, theirs);

        Assert.NotNull(await SuspensionOfAsync(api, them));
        Assert.Null(await SuspensionOfAsync(api, me));
        Assert.True((await AiAccessAsync(mine)).GetProperty("available").GetBoolean());
    }

    // US-092 criterion 5: the API refuses, not only the page, and says until when.
    [Fact]
    public async Task While_paused_a_new_photo_is_AI_SUSPENDED_with_when_it_ends()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "paused-create@example.com");
        await PauseForAWeekAsync(api, learner);

        using var response = await Learners.PostWorldAsync(browser, Learners.Photo(800, 600));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = (await ApiHost.BodyAsync(response)).GetProperty("error");
        Assert.Equal("AI_SUSPENDED", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("details").GetProperty("awaitingReview").GetBoolean());
        Assert.Equal(JsonValueKind.String, error.GetProperty("details").GetProperty("until").ValueKind);
        await using var db = api.Database.Context();
        Assert.False(await db.Worlds.AnyAsync(TestContext.Current.CancellationToken));
    }

    // A retry is an analysis too, of a photo kept from before the pause; US-092 criterion 4: worlds stay readable.
    [Fact]
    public async Task While_paused_a_retry_is_refused_and_runs_nothing_while_worlds_stay_readable()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "paused-retry@example.com");
        var fake = api.Service<FakeAiProvider>();
        fake.Behaviour = "PROVIDER_ERROR";
        var failed = await Learners.AnalysedWorldAsync(api, browser);
        await PauseForAWeekAsync(api, learner);
        fake.Behaviour = "ALLOWED";
        fake.Calls.Clear();

        using var response = await browser.PostAsync(new Uri($"/v1/worlds/{failed}/retry", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        Assert.Equal("AI_SUSPENDED", await ApiHost.ErrorAsync(response, HttpStatusCode.Forbidden));
        await api.Service<AnalysisRunner>().SettledAsync(failed);
        Assert.Empty(fake.Calls);
        Assert.Equal("FAILED", (await WorldAsync(browser, failed)).GetProperty("status").GetString());
        using var list = await browser.GetAsync(new Uri("/v1/worlds", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // FR-107: every active admin is told, and the email says nothing about who it is.
    [Fact]
    public async Task A_case_that_needs_review_is_emailed_to_every_active_admin_without_naming_the_learner()
    {
        await using var api = await ApiHost.StartAsync(new Dictionary<string, string> { ["ADMIN_ALERT_EMAIL"] = AlertAddress });
        await AdminAsync(api, "admin-one@example.com");
        await AdminAsync(api, "admin-two@example.com");
        await AdminAsync(api, "admin-suspended@example.com", AccountStatus.Suspended);
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "needs-review@example.com");
        await ServedPauseAsync(api, learner);
        await EarlierBlocksAsync(api, learner, 2);

        await BlockOneAsync(api, browser);

        var sent = ReviewMail(api);
        Assert.Equal(["admin-one@example.com", "admin-two@example.com"], sent.Select(message => message.To).Order(StringComparer.Ordinal));
        Assert.All(sent, message =>
        {
            Assert.Contains($"{ApiHost.WebOrigin}/admin/review-queue", message.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(learner.ToString(), message.Text + message.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("needs-review@example.com", message.Text + message.Html, StringComparison.Ordinal);
        });
    }

    // A level 1 pause ends by itself, so nobody has to act on it.
    [Fact]
    public async Task A_pause_that_ends_by_itself_is_not_emailed()
    {
        await using var api = await ApiHost.StartAsync();
        await AdminAsync(api, "admin@example.com");
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "level-one@example.com");
        await EarlierBlocksAsync(api, learner, 2);

        await BlockOneAsync(api, browser);

        Assert.NotNull(await SuspensionOfAsync(api, learner));
        Assert.Empty(ReviewMail(api));
    }

    // Admins are promoted by a deployment script that may not have run, so the alert has somewhere to go.
    [Fact]
    public async Task With_no_admin_a_case_goes_to_ADMIN_ALERT_EMAIL()
    {
        await using var api = await ApiHost.StartAsync(new Dictionary<string, string> { ["ADMIN_ALERT_EMAIL"] = AlertAddress });
        var browser = await Learners.VerifiedAsync(api, "illegal-alert@example.com");
        api.Service<FakeAiProvider>().BlockCategory = BlockCategory.SuspectedIllegalMaterial;

        await BlockOneAsync(api, browser);

        Assert.Equal([AlertAddress], ReviewMail(api).Select(message => message.To));
    }

    // With nobody to tell, the case is logged rather than sent anywhere, and the pause stands.
    [Fact]
    public async Task With_no_admin_and_no_alert_address_nothing_is_sent_and_the_pause_stands()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "nobody@example.com");
        api.Service<FakeAiProvider>().BlockCategory = BlockCategory.SuspectedIllegalMaterial;

        await BlockOneAsync(api, browser);

        Assert.Empty(ReviewMail(api));
        Assert.NotNull(await SuspensionOfAsync(api, learner));
    }

    // The email only hurries an admin along; a mail server that is down must not undo the pause.
    [Fact]
    public async Task A_mail_server_that_refuses_leaves_the_pause_in_force()
    {
        await using var api = await ApiHost.StartAsync();
        await AdminAsync(api, "admin@example.com");
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "smtp-down@example.com");
        await ServedPauseAsync(api, learner);
        await EarlierBlocksAsync(api, learner, 2);
        api.Mail.Refuses = true;

        var world = await BlockOneAsync(api, browser);

        var raised = await SuspensionOfAsync(api, learner);
        Assert.Equal(SuspensionReason.RepeatedAfterSuspension, raised?.Reason);
        Assert.Null(raised?.EndsAt);
        Assert.Equal("BLOCKED", (await WorldAsync(browser, world)).GetProperty("failureReason").GetString());
    }

    /// <summary>Blocks one photo for real, through the whole analysis; answers its world.</summary>
    private static async Task<Guid> BlockOneAsync(ApiHost api, HttpClient browser)
    {
        var fake = api.Service<FakeAiProvider>();
        fake.Behaviour = "BLOCKED";
        var world = await Learners.AnalysedWorldAsync(api, browser);
        fake.Behaviour = "ALLOWED";
        return world;
    }

    /// <summary>Blocks the learner already had, as many as the test needs.</summary>
    private static async Task EarlierBlocksAsync(ApiHost api, Guid learner, int count, int daysAgo = 1)
    {
        await using var db = api.Database.Context();
        var at = api.Clock.GetUtcNow().AddDays(-daysAgo);
        db.PhotoBlocks.AddRange(Enumerable.Range(0, count).Select(_ => new PhotoBlock { LearnerId = learner, Category = BlockCategory.Violence, CreatedAt = at }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A level 1 pause that ran out three weeks ago.</summary>
    private static Task ServedPauseAsync(ApiHost api, Guid learner)
    {
        var now = api.Clock.GetUtcNow();
        return SuspendAsync(api, learner, now.AddDays(-30), now.AddDays(-23));
    }

    private static Task PauseForAWeekAsync(ApiHost api, Guid learner)
    {
        var now = api.Clock.GetUtcNow();
        return SuspendAsync(api, learner, now, now.AddDays(7));
    }

    private static async Task SuspendAsync(ApiHost api, Guid learner, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        await using var db = api.Database.Context();
        db.AiSuspensions.Add(new AiSuspension { LearnerId = learner, Reason = SuspensionReason.RepeatedBlocks, StartsAt = startsAt, EndsAt = endsAt });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The learner's latest suspension, or none.</summary>
    private static async Task<AiSuspension?> SuspensionOfAsync(ApiHost api, Guid learner)
    {
        await using var db = api.Database.Context();
        return await db.AiSuspensions.Where(suspension => suspension.LearnerId == learner)
            .OrderByDescending(suspension => suspension.StartsAt)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>An admin, made by signing up and promoting: these tests are not about how one is created.</summary>
    private static async Task AdminAsync(ApiHost api, string email, AccountStatus status = AccountStatus.Active)
    {
        using var browser = api.Browser();
        var id = await ApiHost.SignUpAsync(browser, email);
        await using var db = api.Database.Context();
        await db.Users.Where(user => user.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.Role, UserRole.Admin).SetProperty(user => user.Status, status),
                TestContext.Current.CancellationToken);
    }

    private static List<Modules.Notifications.Contracts.MailMessage> ReviewMail(ApiHost api) =>
        [.. api.Mail.Sent.Where(message => message.Subject.Contains("รอการตรวจสอบ", StringComparison.Ordinal))];

    private static async Task<JsonElement> WorldAsync(HttpClient browser, Guid world) =>
        await ApiHost.BodyAsync(await browser.GetAsync(new Uri($"/v1/worlds/{world}", UriKind.Relative), TestContext.Current.CancellationToken));

    private static async Task<JsonElement> AiAccessAsync(HttpClient browser)
    {
        using var me = await browser.GetAsync(new Uri("/v1/me", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (await ApiHost.BodyAsync(me)).GetProperty("aiAccess");
    }
}
