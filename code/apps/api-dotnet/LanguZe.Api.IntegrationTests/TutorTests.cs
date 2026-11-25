using System.Net;
using System.Text.Json;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Tutor.Features.GetConversation;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// The tutor conversation and the day's messages where only the database can set them up, as
/// tutor.e2e-spec.ts does through Prisma: a conversation written straight to the table, as
/// sending one will, and a day's count of messages used (FR-070, FR-071, FR-076, FR-080, V1).
/// </summary>
public sealed class TutorTests
{
    [Fact]
    public async Task The_newest_messages_come_first()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-newest@example.com");
        await SaidAsync(api, learner, 3);

        var page = await ConversationAsync(browser);

        Assert.Equal(["message 2", "message 1", "message 0"], Contents(page));
        Assert.Equal("LEARNER", page.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    // A long conversation is read a page at a time, newest page first.
    [Fact]
    public async Task Older_messages_come_a_page_at_a_time()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-pages@example.com");
        await SaidAsync(api, learner, GetConversationHandler.MessagesPerPage + 5);

        var first = await ConversationAsync(browser);
        Assert.Equal(GetConversationHandler.MessagesPerPage, first.GetProperty("messages").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.Equal(first.GetProperty("messages")[GetConversationHandler.MessagesPerPage - 1].GetProperty("id").GetString(), cursor);

        var older = await ConversationAsync(browser, cursor);

        Assert.Equal(["message 4", "message 3", "message 2", "message 1", "message 0"], Contents(older));
        Assert.Equal(JsonValueKind.Null, older.GetProperty("nextCursor").ValueKind);
    }

    // Exactly a page: nothing older, so no cursor.
    [Fact]
    public async Task A_conversation_of_exactly_one_page_has_no_cursor()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-one-page@example.com");
        await SaidAsync(api, learner, GetConversationHandler.MessagesPerPage);

        var page = await ConversationAsync(browser);

        Assert.Equal(GetConversationHandler.MessagesPerPage, page.GetProperty("messages").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    // V6: one conversation per learner, and it is nobody else's business.
    [Fact]
    public async Task A_learner_sees_nothing_of_anyone_elses()
    {
        await using var api = await ApiHost.StartAsync();
        var mine = await Learners.VerifiedAsync(api, "tutor-mine@example.com");
        var (_, theirs) = await Learners.VerifiedLearnerAsync(api, "tutor-theirs@example.com");
        await SaidAsync(api, theirs, 2);

        Assert.Empty(Contents(await ConversationAsync(mine)));
    }

    // Prisma's cursor is a time, not a message of this learner's: another learner's message
    // only says where to start, and still shows nothing of theirs.
    [Fact]
    public async Task A_cursor_from_another_learner_starts_at_its_time_and_shows_only_ones_own()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, mine) = await Learners.VerifiedLearnerAsync(api, "tutor-cursor-mine@example.com");
        var (_, theirs) = await Learners.VerifiedLearnerAsync(api, "tutor-cursor-theirs@example.com");
        var now = api.Clock.GetUtcNow();
        Guid cursor;
        await using (var db = api.Database.Context())
        {
            db.TutorMessages.Add(new TutorMessage { LearnerId = mine, Role = TutorRole.Learner, Content = "older", CreatedAt = now.AddSeconds(-30) });
            db.TutorMessages.Add(new TutorMessage { LearnerId = mine, Role = TutorRole.Tutor, Content = "newer", CreatedAt = now.AddSeconds(-10) });
            var theirMessage = new TutorMessage { LearnerId = theirs, Role = TutorRole.Learner, Content = "theirs", CreatedAt = now.AddSeconds(-20) };
            db.TutorMessages.Add(theirMessage);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            cursor = theirMessage.Id;
        }

        // The first row up to that time is skipped, as Prisma's skip: 1 skips the cursor itself.
        var page = await ConversationAsync(browser, cursor.ToString());

        Assert.Empty(Contents(page));
    }

    [Fact]
    public async Task Clearing_deletes_the_conversation_and_nobody_elses()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, mine) = await Learners.VerifiedLearnerAsync(api, "tutor-clear-mine@example.com");
        var (_, theirs) = await Learners.VerifiedLearnerAsync(api, "tutor-clear-theirs@example.com");
        await SaidAsync(api, mine, 4);
        await SaidAsync(api, theirs, 3);

        var response = await browser.DeleteAsync(new Uri("/v1/tutor/messages", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Contents(await ConversationAsync(browser)));
        await using var db = api.Database.Context();
        Assert.Equal(0, await db.TutorMessages.CountAsync(message => message.LearnerId == mine, TestContext.Current.CancellationToken));
        Assert.Equal(3, await db.TutorMessages.CountAsync(message => message.LearnerId == theirs, TestContext.Current.CancellationToken));
    }

    // US-073 criterion 2: the messages were sent; forgetting them does not unsend them.
    [Fact]
    public async Task Clearing_does_not_give_todays_messages_back()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-clear-usage@example.com");
        await UsedAsync(api, learner, Today(api), 7);

        await browser.DeleteAsync(new Uri("/v1/tutor/messages", UriKind.Relative), TestContext.Current.CancellationToken);

        var usage = await UsageAsync(browser);
        Assert.Equal((23, 30), (usage.GetProperty("messagesLeft").GetInt32(), usage.GetProperty("messagesLimit").GetInt32()));
    }

    [Fact]
    public async Task Usage_counts_down_as_messages_are_used_and_never_below_nothing()
    {
        await using var api = await ApiHost.StartAsync(new Dictionary<string, string> { ["DAILY_TUTOR_MESSAGE_LIMIT"] = "5" });
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-usage-down@example.com");
        await UsedAsync(api, learner, Today(api), 4);

        Assert.Equal((1, 5), Messages(await UsageAsync(browser)));

        await using (var db = api.Database.Context())
        {
            await db.TutorDailyUsage.Where(usage => usage.LearnerId == learner)
                .ExecuteUpdateAsync(setters => setters.SetProperty(usage => usage.MessagesUsed, (short)9), TestContext.Current.CancellationToken);
        }

        // A limit lowered after the day's messages were used leaves none, not fewer than none.
        Assert.Equal((0, 5), Messages(await UsageAsync(browser)));
    }

    // Yesterday's messages are yesterday's problem (V1), and so is anyone else's count.
    [Fact]
    public async Task Usage_ignores_a_count_from_another_day_or_another_learner()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-usage-yesterday@example.com");
        var (_, other) = await Learners.VerifiedLearnerAsync(api, "tutor-usage-other@example.com");
        await UsedAsync(api, learner, Today(api).AddDays(-1), 30);
        await UsedAsync(api, other, Today(api), 30);

        Assert.Equal((30, 30), Messages(await UsageAsync(browser)));
    }

    // V1. For the last seven hours of every UTC day Bangkok is already on the next one: the
    // count read is the Bangkok day's, and both limits reset at the next Bangkok midnight.
    [Fact]
    public async Task Usage_reads_the_Bangkok_day_not_the_servers()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-usage-bangkok@example.com");
        api.Clock.Advance(DateTimeOffset.Parse("2026-10-02T18:00:00Z", System.Globalization.CultureInfo.InvariantCulture) - api.Clock.GetUtcNow());
        await UsedAsync(api, learner, new DateOnly(2026, 10, 2), 9);
        await UsedAsync(api, learner, new DateOnly(2026, 10, 3), 2);

        var usage = await UsageAsync(browser);

        Assert.Equal((28, 30), Messages(usage));
        Assert.Equal("2026-10-03T17:00:00.000Z", usage.GetProperty("resetsAt").GetString());
    }

    // The analyses of the Bangkok day too, counted as taking one counts them (U2).
    [Fact]
    public async Task Usage_counts_the_analyses_of_the_Bangkok_day()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "tutor-usage-analyses@example.com");
        var now = api.Clock.GetUtcNow();
        var dayStarted = LanguZe.Api.Platform.Time.BangkokDay.StartOf(now);
        await using (var db = api.Database.Context())
        {
            db.Analyses.AddRange(
                new Analysis { LearnerId = learner, Status = AnalysisStatus.Succeeded, StartedAt = dayStarted },
                new Analysis { LearnerId = learner, Status = AnalysisStatus.Blocked, StartedAt = now },
                new Analysis { LearnerId = learner, Status = AnalysisStatus.InProgress, StartedAt = now },
                new Analysis { LearnerId = learner, Status = AnalysisStatus.Failed, StartedAt = now },
                new Analysis { LearnerId = learner, Status = AnalysisStatus.Succeeded, StartedAt = dayStarted.AddMilliseconds(-1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var usage = await UsageAsync(browser);

        Assert.Equal((7, 10), (usage.GetProperty("analysesLeft").GetInt32(), usage.GetProperty("analysesLimit").GetInt32()));
    }

    /// <summary>Writes a conversation straight to the table, a second apart, alternating learner and tutor, as sending one will.</summary>
    private static async Task SaidAsync(ApiHost api, Guid learner, int count)
    {
        var now = api.Clock.GetUtcNow();
        await using var db = api.Database.Context();
        for (var i = 0; i < count; i++)
        {
            db.TutorMessages.Add(new TutorMessage
            {
                LearnerId = learner,
                Role = i % 2 == 0 ? TutorRole.Learner : TutorRole.Tutor,
                Content = $"message {i}",
                CreatedAt = now.AddSeconds(i - count),
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task UsedAsync(ApiHost api, Guid learner, DateOnly day, short used)
    {
        await using var db = api.Database.Context();
        db.TutorDailyUsage.Add(new TutorDailyUsage { LearnerId = learner, Day = day, MessagesUsed = used });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static DateOnly Today(ApiHost api) =>
        DateOnly.FromDateTime(api.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);

    private static async Task<JsonElement> ConversationAsync(HttpClient browser, string? before = null)
    {
        var response = await browser.GetAsync(new Uri(before is null ? "/v1/tutor/messages" : $"/v1/tutor/messages?before={before}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiHost.BodyAsync(response);
    }

    private static async Task<JsonElement> UsageAsync(HttpClient browser)
    {
        var response = await browser.GetAsync(new Uri("/v1/me/usage", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiHost.BodyAsync(response);
    }

    private static string[] Contents(JsonElement page) =>
        [.. page.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("content").GetString()!)];

    private static (int Left, int Limit) Messages(JsonElement usage) =>
        (usage.GetProperty("messagesLeft").GetInt32(), usage.GetProperty("messagesLimit").GetInt32());
}
