using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Learning.Shared;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// Playing a world where only the database or a clock shows what happened, as
/// sessions.e2e-spec.ts and answering.e2e-spec.ts check through Prisma: XP, the attempts and
/// mastery kept, a session left for a day (V17), a word mastered across days, and a world still
/// being analysed.
/// </summary>
public sealed class LearningTests
{
    // FR-030: a world with no words yet has nothing to play.
    [Fact]
    public async Task A_world_still_being_analysed_is_WORLD_NOT_READY()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "not-ready@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        await using (var db = api.Database.Context())
        {
            await db.Worlds.Where(row => row.Id == world).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, WorldStatus.Analyzing), TestContext.Current.CancellationToken);
        }

        using var response = await StartAsync(browser, world);

        Assert.Equal("WORLD_NOT_READY", await ApiHost.ErrorAsync(response, HttpStatusCode.Conflict));
    }

    // V17: a session left for a day is over. Read directly, it is closed on the way, and this
    // answer still shows it as it was read, as NestJS's does; the next read shows it abandoned.
    [Fact]
    public async Task A_session_left_for_a_day_is_given_up()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "day-old@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var session = await JsonOfAsync(await StartAsync(browser, world));
        api.Clock.Advance(SessionReader.OpenFor);

        var read = await JsonOfAsync(await browser.GetAsync(new Uri($"/v1/sessions/{IdOf(session)}", UriKind.Relative), TestContext.Current.CancellationToken));
        var again = await JsonOfAsync(await browser.GetAsync(new Uri($"/v1/sessions/{IdOf(session)}", UriKind.Relative), TestContext.Current.CancellationToken));
        using var current = await browser.GetAsync(new Uri($"/v1/sessions/current?kind=GAME&worldId={world}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("IN_PROGRESS", read.GetProperty("status").GetString());
        Assert.Equal("ABANDONED", again.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task The_open_game_is_found_while_it_is_only_hours_old_and_given_up_at_a_day()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "hours-old@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var session = await JsonOfAsync(await StartAsync(browser, world));
        var path = new Uri($"/v1/sessions/current?kind=GAME&worldId={world}", UriKind.Relative);

        api.Clock.Advance(SessionReader.OpenFor - TimeSpan.FromMinutes(1));
        using (var stillOpen = await browser.GetAsync(path, TestContext.Current.CancellationToken))
        {
            Assert.Equal(IdOf(session), IdOf(await JsonOfAsync(stillOpen)));
        }

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        using var over = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, over.StatusCode);
        await using var db = api.Database.Context();
        Assert.Equal(SessionStatus.Abandoned, await db.PracticeSessions.Select(row => row.Status).SingleAsync(TestContext.Current.CancellationToken));
    }

    // V17: whatever its row still says, a day-old session takes no more answers.
    [Fact]
    public async Task An_answer_to_a_session_left_for_a_day_is_SESSION_CLOSED()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "day-old-answer@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var session = await JsonOfAsync(await StartAsync(browser, world));
        api.Clock.Advance(SessionReader.OpenFor + TimeSpan.FromSeconds(1));

        using var response = await AnswerAsync(browser, session, new { dontKnow = true });

        Assert.Equal("SESSION_CLOSED", await ApiHost.ErrorAsync(response, HttpStatusCode.Conflict));
        await using var db = api.Database.Context();
        Assert.Equal(SessionStatus.Abandoned, await db.PracticeSessions.Select(row => row.Status).SingleAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Attempts.AnyAsync(TestContext.Current.CancellationToken));
    }

    // FR-060, V4, FR-034: XP for each right answer, once, whatever arrives twice or together.
    [Fact]
    public async Task Xp_is_paid_once_for_each_right_answer()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "xp@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var session = await JsonOfAsync(await StartAsync(browser, world));

        (await AnswerAsync(browser, session, new { answer = "zzzz" })).Dispose();
        Assert.Null(await XpOfAsync(api, learner));

        session = await ReadAsync(browser, session);
        var english = await EnglishOfAsync(api, session);
        var together = await Task.WhenAll(AnswerAsync(browser, session, new { answer = english }), AnswerAsync(browser, session, new { answer = english }));
        Array.ForEach(together, response => response.Dispose());
        (await AnswerAsync(browser, session, new { answer = english })).Dispose();

        Assert.Equal(10, await XpOfAsync(api, learner));
        await using var db = api.Database.Context();
        Assert.Equal(2, await db.Attempts.CountAsync(TestContext.Current.CancellationToken));
    }

    // FR-034, S5: answers racing for one question, six at a time for every question, so that some
    // get past the check for an earlier answer together and the database's unique index decides.
    // The one it refuses shows the recorded result rather than failing.
    [Fact]
    public async Task Answers_racing_for_the_same_question_record_one_and_fail_none()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "race@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var session = await JsonOfAsync(await StartAsync(browser, world));
        List<Guid> questions;
        await using (var db = api.Database.Context())
        {
            questions = await db.SessionQuestions.OrderBy(row => row.Position).Select(row => row.Id).ToListAsync(TestContext.Current.CancellationToken);
        }

        foreach (var question in questions)
        {
            var path = $"/v1/sessions/{IdOf(session)}/questions/{question}/answer";
            var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => browser.PostAsJsonAsync(path, new { dontKnow = true }, TestContext.Current.CancellationToken)));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var results = await Task.WhenAll(responses.Select(ApiHost.BodyAsync));
            Assert.Single(results, result => !result.GetProperty("alreadyAnswered").GetBoolean());
            Array.ForEach(responses, response => response.Dispose());
        }

        await using var after = api.Database.Context();
        Assert.Equal(questions.Count, await after.Attempts.CountAsync(TestContext.Current.CancellationToken));
    }

    // FR-042, SRS 4.1: an attempt keeps what was typed and where the word went; mastery follows it.
    [Fact]
    public async Task Each_answer_is_kept_with_the_level_it_left_the_word_at()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "attempts@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var session = await JsonOfAsync(await StartAsync(browser, world));
        var wrongWord = await EnglishOfAsync(api, session);
        (await AnswerAsync(browser, session, new { answer = "zzzz" })).Dispose();
        session = await ReadAsync(browser, session);
        (await AnswerAsync(browser, session, new { dontKnow = true })).Dispose();

        await using var db = api.Database.Context();
        var attempts = await db.Attempts.OrderBy(attempt => attempt.AnsweredAt).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("zzzz", false, false), (attempts[0].AnswerText, attempts[0].IsDontKnow, attempts[0].IsCorrect));
        Assert.Equal((null, true, false), (attempts[1].AnswerText, attempts[1].IsDontKnow, attempts[1].IsCorrect));
        Assert.All(attempts, attempt => Assert.Equal((null, MasteryLevel.Learning, (short)0), (attempt.LevelBefore, attempt.LevelAfter, attempt.XpAwarded)));
        var word = await db.VocabularyWords.SingleAsync(row => row.English == wrongWord, TestContext.Current.CancellationToken);
        var mastery = await db.WordMastery.SingleAsync(row => row.VocabularyWordId == word.Id, TestContext.Current.CancellationToken);
        Assert.Equal((learner, MasteryLevel.Learning, (short)0), (mastery.LearnerId, mastery.Level, mastery.Streak));
    }

    // V1: the day a word becomes FAMILIAR is Bangkok's, not UTC's.
    [Fact]
    public async Task A_word_becomes_familiar_on_the_learners_day_in_Bangkok()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "bangkok-day@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        // 18:30 in UTC is 01:30 the next morning in Bangkok.
        var now = api.Clock.GetUtcNow();
        var evening = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1).AddHours(18).AddMinutes(30), TimeSpan.Zero);
        api.Clock.Advance(evening - now);

        var english = await AnswerRightTwiceAsync(api, browser, world);

        await using var db = api.Database.Context();
        var mastery = await db.WordMastery.SingleAsync(row => row.Level == MasteryLevel.Familiar, TestContext.Current.CancellationToken);
        Assert.Equal(DateOnly.FromDateTime(evening.UtcDateTime.AddDays(1)), mastery.FamiliarOn);
        Assert.Equal(english, await db.VocabularyWords.Where(row => row.Id == mastery.VocabularyWordId).Select(row => row.English).SingleAsync(TestContext.Current.CancellationToken));
    }

    // SRS 4.1, FR-013: MASTERED takes a later day, and the world list counts it.
    [Fact]
    public async Task A_word_mastered_on_a_later_day_is_counted_by_its_world()
    {
        await using var api = await ApiHost.StartAsync();
        var browser = await Learners.VerifiedAsync(api, "mastered@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        var english = await AnswerRightTwiceAsync(api, browser, world);
        api.Clock.Advance(TimeSpan.FromDays(1));

        await AnswerRightTwiceAsync(api, browser, world, english);

        var list = await JsonOfAsync(await browser.GetAsync(new Uri("/v1/worlds", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal(1, list.EnumerateArray().Single().GetProperty("masteredCount").GetInt32());
        await using var db = api.Database.Context();
        Assert.Equal(MasteryLevel.Mastered, await db.WordMastery.Select(row => row.Level).SingleAsync(row => row == MasteryLevel.Mastered, TestContext.Current.CancellationToken));
    }

    // FR-030: a word already mastered is asked last.
    [Fact]
    public async Task A_mastered_word_is_asked_last()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "mastered-last@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        Guid mastered;
        await using (var db = api.Database.Context())
        {
            mastered = await db.WordOccurrences.Where(row => row.WorldId == world).Select(row => row.VocabularyWordId).FirstAsync(TestContext.Current.CancellationToken);
            db.WordMastery.Add(new WordMastery { VocabularyWordId = mastered, LearnerId = learner, Level = MasteryLevel.Mastered, LastPractisedAt = api.Clock.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await StartAsync(browser, world);

        await using var after = api.Database.Context();
        var last = await after.SessionQuestions.OrderByDescending(row => row.Position).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(mastered, last.VocabularyWordId);
    }

    // US-050 criterion 4, FR-050: a mastered word is not brought back, however badly it once went.
    [Fact]
    public async Task A_review_leaves_out_a_mastered_word_even_one_got_wrong()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "review-mastered@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        Guid learning;
        await using (var db = api.Database.Context())
        {
            var occurrences = await db.WordOccurrences.Where(row => row.WorldId == world).Take(2).ToListAsync(TestContext.Current.CancellationToken);
            var at = api.Clock.GetUtcNow();
            db.WordMastery.Add(new WordMastery { VocabularyWordId = occurrences[0].VocabularyWordId, LearnerId = learner, Level = MasteryLevel.Mastered, LastPractisedAt = at });
            db.Attempts.Add(new Attempt { LearnerId = learner, VocabularyWordId = occurrences[0].VocabularyWordId, OccurrenceId = occurrences[0].Id, AnswerText = "zzz", IsCorrect = false, LevelAfter = MasteryLevel.Mastered, AnsweredAt = at });
            db.WordMastery.Add(new WordMastery { VocabularyWordId = occurrences[1].VocabularyWordId, LearnerId = learner, Level = MasteryLevel.Learning, LastPractisedAt = at });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            learning = occurrences[1].Id;
        }

        var review = await JsonOfAsync(await browser.PostAsJsonAsync("/v1/sessions", new { kind = "REVIEW" }, TestContext.Current.CancellationToken));

        Assert.Equal(1, review.GetProperty("questionCount").GetInt32());
        await using var after = api.Database.Context();
        Assert.Equal(learning, await after.SessionQuestions.Select(row => row.OccurrenceId).SingleAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Answers one word right in two games in a row, the first asked unless <paramref name="english"/> names it; answers its English.</summary>
    private static async Task<string> AnswerRightTwiceAsync(ApiHost api, HttpClient browser, Guid world, string? english = null)
    {
        for (var game = 0; game < 2; game++)
        {
            var session = await JsonOfAsync(await StartAsync(browser, world));
            while (session.GetProperty("nextQuestion").ValueKind != JsonValueKind.Null)
            {
                var asked = await EnglishOfAsync(api, session);
                english ??= asked;
                (await AnswerAsync(browser, session, asked == english ? new { answer = asked } : new { answer = "zzzz" })).Dispose();
                session = await ReadAsync(browser, session);
            }
        }

        return english!;
    }

    private static Task<HttpResponseMessage> StartAsync(HttpClient browser, Guid world) =>
        browser.PostAsJsonAsync("/v1/sessions", new { kind = "GAME", worldId = world }, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> AnswerAsync(HttpClient browser, JsonElement session, object body) =>
        browser.PostAsJsonAsync($"/v1/sessions/{IdOf(session)}/questions/{session.GetProperty("nextQuestion").GetProperty("id").GetString()}/answer", body, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReadAsync(HttpClient browser, JsonElement session) =>
        await JsonOfAsync(await browser.GetAsync(new Uri($"/v1/sessions/{IdOf(session)}", UriKind.Relative), TestContext.Current.CancellationToken));

    /// <summary>The English of the word the session's next question asks about, which only the database may tell.</summary>
    private static async Task<string> EnglishOfAsync(ApiHost api, JsonElement session)
    {
        var question = Guid.Parse(session.GetProperty("nextQuestion").GetProperty("id").GetString()!);
        await using var db = api.Database.Context();
        return await (from row in db.SessionQuestions
                      where row.Id == question
                      join word in db.VocabularyWords on row.VocabularyWordId equals (Guid?)word.Id
                      select word.English).SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int?> XpOfAsync(ApiHost api, Guid learner)
    {
        await using var db = api.Database.Context();
        return await db.LearnerXp.Where(row => row.LearnerId == learner).Select(row => (int?)row.TotalXp).SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> JsonOfAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"Expected success, got {(int)response.StatusCode}.");
            return await ApiHost.BodyAsync(response);
        }
    }

    private static string IdOf(JsonElement session) => session.GetProperty("id").GetString()!;
}
