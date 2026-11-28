using System.Text.Json;
using System.Text.RegularExpressions;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Tutor.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// What the tutor may look up (tutor-tools.e2e-spec.ts; US-071, FR-072). These run against the
/// database, because the whole point of them is which rows they can reach.
/// </summary>
public sealed partial class TutorToolsTests
{
    // FR-072: no tool takes a learner identifier, so nothing the model writes reaches another learner.
    [Fact]
    public async Task The_model_has_no_way_to_name_a_learner()
    {
        await using var api = await ApiHost.StartAsync();
        var learner = await Learners.VerifiedLearnerAsync(api, "tools-names@example.com");

        await WithToolsAsync(api, learner.Id, tools =>
        {
            Assert.Equal(["weak_words", "recent_mistakes", "latest_game", "latest_finished_game", "word_details"], tools.Select(tool => tool.Name));
            foreach (var tool in tools)
            {
                Assert.DoesNotMatch(LearnerWords(), string.Join(' ', tool.Parameters.Select(parameter => parameter.Name)));
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Weak_words_are_the_words_still_being_learned_with_how_often_they_went_wrong()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-weak@example.com");
        await WordAsync(api, learner, "sofa", MasteryLevel.Familiar, practisedAt: api.Clock.GetUtcNow().AddDays(-3), wrongAnswers: [("zzz", api.Clock.GetUtcNow())], rightAnswers: 2);
        await WordAsync(api, learner, "rug", MasteryLevel.Learning, practisedAt: api.Clock.GetUtcNow().AddDays(-1));
        await WordAsync(api, learner, "lamp", MasteryLevel.Learning, practisedAt: api.Clock.GetUtcNow().AddDays(-2));
        await WordAsync(api, learner, "clock", MasteryLevel.Mastered);

        var result = (WeakWordsResult)await RunAsync(api, learner, "weak_words", new { count = 5 });

        // The least known first: LEARNING before FAMILIAR, however long ago, then the longest unpractised first.
        Assert.Equal(["lamp", "rug", "sofa"], result.Words.Select(word => word.English));
        Assert.Equal(("FAMILIAR", 1, "ความหมายของ sofa"), (result.Words[2].Mastery, result.Words[2].Mistakes, result.Words[2].ThaiMeaning));
    }

    [Theory]
    [InlineData("""{"count":5000}""", 20)]
    [InlineData("""{"count":2.9}""", 2)]
    [InlineData("""{"count":-1}""", 10)]
    [InlineData("""{"count":0}""", 10)]
    [InlineData("""{"count":"many"}""", 10)]
    [InlineData("""{"count":null}""", 10)]
    [InlineData("""{"count":1e400}""", 10)]
    [InlineData("""{}""", 10)]
    public async Task A_count_is_made_safe_rather_than_refused(string input, int rows)
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-count@example.com");
        await ManyWordsAsync(api, learner, 25);

        var weak = (WeakWordsResult)await RunAsync(api, learner, "weak_words", input);
        var mistakes = (RecentMistakesResult)await RunAsync(api, learner, "recent_mistakes", input);

        Assert.Equal((rows, rows), (weak.Words.Count, mistakes.Mistakes.Count));
    }

    [Fact]
    public async Task Weak_words_read_nobody_elses()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, mine) = await Learners.VerifiedLearnerAsync(api, "tools-weak-mine@example.com");
        var (_, theirs) = await Learners.VerifiedLearnerAsync(api, "tools-weak-theirs@example.com");
        await WordAsync(api, theirs, "sofa", MasteryLevel.Learning, wrongAnswers: [("zzz", api.Clock.GetUtcNow())]);

        Assert.Empty(((WeakWordsResult)await RunAsync(api, mine, "weak_words", new { })).Words);
        Assert.Empty(((RecentMistakesResult)await RunAsync(api, mine, "recent_mistakes", new { })).Mistakes);
    }

    // "The latest game" is the one played last, finished or not (decided 2026-11-28); review sessions are not games.
    [Fact]
    public async Task Latest_game_reads_the_game_played_last_even_unfinished()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game-unfinished@example.com");
        var now = api.Clock.GetUtcNow();
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning);
        await WordAsync(api, learner, "rain", MasteryLevel.Learning);
        await GameAsync(api, learner, SessionStatus.Completed, now.AddDays(-2), now.AddDays(-2).AddMinutes(5), ("cloud", "sky", false, now.AddDays(-2).AddMinutes(1)));
        await GameAsync(api, learner, SessionStatus.InProgress, now.AddHours(-1), null, ("rain", "rian", false, now.AddMinutes(-30)));
        await GameAsync(api, learner, SessionStatus.Completed, now.AddMinutes(-10), now.AddMinutes(-5), ("cloud", "clod", false, now.AddMinutes(-8)), kind: SessionKind.Review);

        var latest = Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_game", new { }));

        Assert.Equal((true, false), (latest.HasGame, latest.LatestGame!.Finished));
        Assert.Null(latest.EarlierGameWithMistakes);
        Assert.Equal("rian", Assert.Single(latest.LatestGame.Mistakes).Answered);
    }

    [Fact]
    public async Task Games_are_ordered_by_their_last_answer_not_by_when_they_started()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game-order@example.com");
        var now = api.Clock.GetUtcNow();
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning);
        await WordAsync(api, learner, "rain", MasteryLevel.Learning);
        await GameAsync(api, learner, SessionStatus.InProgress, now.AddHours(-5), null, ("cloud", "sky", false, now.AddMinutes(-5)));
        await GameAsync(api, learner, SessionStatus.Abandoned, now.AddHours(-2), null, ("rain", "rian", false, now.AddHours(-2)));

        var latest = Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_game", new { }));

        Assert.Equal("sky", Assert.Single(latest.LatestGame!.Mistakes).Answered);
    }

    [Fact]
    public async Task A_latest_game_without_mistakes_steps_back_to_an_earlier_game_that_has_some()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game-back@example.com");
        var now = api.Clock.GetUtcNow();
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning);
        await WordAsync(api, learner, "rain", MasteryLevel.Learning);
        await GameAsync(api, learner, SessionStatus.Completed, now.AddDays(-2), now.AddDays(-2).AddMinutes(5), ("cloud", "sky", false, now.AddDays(-2).AddMinutes(1)));
        await GameAsync(api, learner, SessionStatus.Abandoned, now.AddDays(-1), null, ("rain", "rain", true, now.AddDays(-1).AddMinutes(1)));
        await GameAsync(api, learner, SessionStatus.InProgress, now.AddMinutes(-10), null);

        var latest = Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_game", new { }));

        Assert.Equal((BangkokDay(now.AddMinutes(-10)), false, 0), (latest.LatestGame!.PlayedOn, latest.LatestGame.Finished, latest.LatestGame.Mistakes.Count));
        Assert.Equal((BangkokDay(now.AddDays(-2).AddMinutes(1)), true, 1), (latest.EarlierGameWithMistakes!.PlayedOn, latest.EarlierGameWithMistakes.Finished, latest.GamesBetween));
        Assert.Equal("sky", Assert.Single(latest.EarlierGameWithMistakes.Mistakes).Answered);
    }

    // Only a question about a finished game is answered from finished games alone.
    [Fact]
    public async Task The_latest_finished_game_skips_unfinished_games_and_steps_back_the_same_way()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game-finished@example.com");
        var now = api.Clock.GetUtcNow();
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning);
        await WordAsync(api, learner, "rain", MasteryLevel.Learning);
        await GameAsync(api, learner, SessionStatus.Completed, now.AddDays(-3), now.AddDays(-3).AddMinutes(5), ("cloud", "sky", false, now.AddDays(-3).AddMinutes(1)));
        await GameAsync(api, learner, SessionStatus.Completed, now.AddDays(-2), now.AddDays(-2).AddMinutes(5), ("rain", "rain", true, now.AddDays(-2).AddMinutes(1)));
        await GameAsync(api, learner, SessionStatus.InProgress, now.AddHours(-1), null, ("rain", "rian", false, now.AddMinutes(-30)));

        var finished = Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_finished_game", new { }));

        Assert.Equal((BangkokDay(now.AddDays(-2).AddMinutes(1)), true, 0), (finished.LatestGame!.PlayedOn, finished.LatestGame.Finished, finished.LatestGame.Mistakes.Count));
        Assert.Equal((BangkokDay(now.AddDays(-3).AddMinutes(1)), 0), (finished.EarlierGameWithMistakes!.PlayedOn, finished.GamesBetween));
        Assert.Equal("sky", Assert.Single(finished.EarlierGameWithMistakes.Mistakes).Answered);
    }

    [Fact]
    public async Task No_game_is_told_apart_from_games_without_mistakes()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game-none@example.com");
        var now = api.Clock.GetUtcNow();
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning, wrongAnswers: [("sky", now)]);
        async Task<LatestGameResult> ReadAsync() =>
            Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_game", new { }));

        var none = await ReadAsync();
        Assert.Equal((false, (PlayedGame?)null, (PlayedGame?)null, 0), (none.HasGame, none.LatestGame, none.EarlierGameWithMistakes, none.GamesBetween));

        await GameAsync(api, learner, SessionStatus.Completed, now.AddMinutes(-5), now, ("cloud", "cloud", true, now.AddMinutes(-4)));
        var clean = await ReadAsync();
        Assert.Equal((true, BangkokDay(now.AddMinutes(-4)), true), (clean.HasGame, clean.LatestGame!.PlayedOn, clean.LatestGame.Finished));
        Assert.Empty(clean.LatestGame.Mistakes);
        Assert.Null(clean.EarlierGameWithMistakes);
    }

    // The learner is told which photo the game was of, and the day as they count it (decided 2026-11-28).
    [Fact]
    public async Task The_game_is_named_by_its_world_and_its_Bangkok_date_and_a_deleted_world_by_no_name()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game-world@example.com");
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning);
        var lateEvening = DateTimeOffset.Parse("2026-11-19T18:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var worldId = await GameAsync(api, learner, SessionStatus.InProgress, lateEvening.AddMinutes(-5), null, ("cloud", "sky", false, lateEvening), world: "ห้องครัว");
        async Task<LatestGameResult> ReadAsync() =>
            Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_game", new { }));

        var named = await ReadAsync();
        Assert.Equal(("ห้องครัว", "2026-11-20", (PlayedGame?)null), (named.LatestGame!.World, named.LatestGame.PlayedOn, named.EarlierGameWithMistakes));

        await using (var db = api.Database.Context())
        {
            db.Worlds.Remove(db.Worlds.Single(world => world.Id == worldId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var deleted = await ReadAsync();
        Assert.Equal(((string?)null, "sky"), (deleted.LatestGame!.World, Assert.Single(deleted.LatestGame.Mistakes).Answered));
    }

    // US-071 criterion 1: the tutor explains from what the learner actually typed.
    [Fact]
    public async Task Recent_mistakes_keep_what_the_learner_typed_newest_first()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-mistakes@example.com");
        await WordAsync(api, learner, "sofa", MasteryLevel.Learning, wrongAnswers:
        [
            ("sofaa", DateTimeOffset.Parse("2026-09-24T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            (null, DateTimeOffset.Parse("2026-09-25T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
        ]);

        var result = (RecentMistakesResult)await RunAsync(api, learner, "recent_mistakes", new { count = 10 });

        // Giving up is reported as itself, not as a wrong answer they typed.
        Assert.Equal(
            [(null, true), ("sofaa", false)],
            result.Mistakes.Select(mistake => (mistake.Answered, mistake.SaidTheyDidNotKnow)));
        Assert.Equal("sofa", result.Mistakes[0].English);
    }

    // US-071 criterion 3: with no mistakes, the tutor is told so, not left to guess.
    [Fact]
    public async Task Recent_mistakes_say_plainly_that_there_are_none()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-no-mistakes@example.com");

        Assert.Empty(((RecentMistakesResult)await RunAsync(api, learner, "recent_mistakes", new { })).Mistakes);
    }

    [Fact]
    public async Task Word_details_give_one_of_the_learners_words_and_how_they_answered_it()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-details@example.com");
        await WordAsync(api, learner, "sofa", MasteryLevel.Familiar, wrongAnswers: [("sofaa", api.Clock.GetUtcNow())]);

        var result = Assert.IsType<WordDetails>(await RunAsync(api, learner, "word_details", new { word = "sofa" }));

        Assert.Equal((true, "sofa", "A1", "FAMILIAR", "The sofa is here."), (result.Found, result.English, result.CefrLevel, result.Mastery, result.ExampleSentence));
        Assert.Equal(("sofaa", false, false), (Assert.Single(result.RecentAnswers).Answered, result.RecentAnswers[0].Correct, result.RecentAnswers[0].SaidTheyDidNotKnow));
    }

    // A word never answered has no mastery, and is NEW.
    [Fact]
    public async Task A_word_never_answered_is_NEW()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-details-new@example.com");
        await WordAsync(api, learner, "sofa", level: null);

        var result = Assert.IsType<WordDetails>(await RunAsync(api, learner, "word_details", new { word = "sofa" }));

        Assert.Equal(("NEW", 0), (result.Mastery, result.RecentAnswers.Count));
    }

    // SRS 4.2: the model writing "the sofa" still finds the learner's sofa.
    [Fact]
    public async Task Word_details_find_a_word_however_the_model_spells_the_request()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-details-spelling@example.com");
        await WordAsync(api, learner, "sofa", MasteryLevel.Learning);

        Assert.IsType<WordDetails>(await RunAsync(api, learner, "word_details", new { word = "  The Sofa " }));
    }

    // FR-075: saying "not yours" is what stops a history being invented for it.
    [Fact]
    public async Task Word_details_say_a_word_is_not_the_learners_rather_than_inventing_one()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, mine) = await Learners.VerifiedLearnerAsync(api, "tools-details-mine@example.com");
        var (_, theirs) = await Learners.VerifiedLearnerAsync(api, "tools-details-theirs@example.com");
        await WordAsync(api, theirs, "sofa", MasteryLevel.Learning);

        var result = Assert.IsType<WordNotFound>(await RunAsync(api, mine, "word_details", new { word = "sofa" }));

        Assert.Equal((false, "The learner does not have this word."), (result.Found, result.Error));
    }

    [Theory]
    [InlineData("""{"word":""}""")]
    [InlineData("""{"word":"   "}""")]
    [InlineData("""{"word":42}""")]
    [InlineData("""{"word":null}""")]
    [InlineData("""{}""")]
    public async Task Word_details_refuse_an_empty_request_with_an_explanation_not_data(string input)
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-details-empty@example.com");

        var result = Assert.IsType<ToolRefusal>(await RunAsync(api, learner, "word_details", input));

        Assert.Equal("Give the English word to look up.", result.Error);
    }

    /// <summary>A world with one word, and whatever history the test wants on it.</summary>
    internal static async Task WordAsync(ApiHost api, Guid learner, string english, MasteryLevel? level, DateTimeOffset? practisedAt = null, (string? Text, DateTimeOffset At)[]? wrongAnswers = null, int rightAnswers = 0)
    {
        await using var db = api.Database.Context();
        var world = new World { Id = Guid.CreateVersion7(), LearnerId = learner, Name = "ห้อง", Status = WorldStatus.Ready };
        var word = new VocabularyWord { Id = Guid.CreateVersion7(), LearnerId = learner, English = english, ThaiMeaning = $"ความหมายของ {english}" };
        db.Worlds.Add(world);
        db.VocabularyWords.Add(word);
        db.WordOccurrences.Add(new WordOccurrence
        {
            WorldId = world.Id,
            VocabularyWordId = word.Id,
            ThaiMeaning = word.ThaiMeaning,
            BoxX = 0.1,
            BoxY = 0.1,
            BoxWidth = 0.2,
            BoxHeight = 0.2,
            ExampleSentence = $"The {english} is here.",
            CefrLevel = CefrLevel.A1,
            AcceptedVariants = [],
        });
        if (level is { } known)
        {
            db.WordMastery.Add(new WordMastery { VocabularyWordId = word.Id, LearnerId = learner, Level = known, LastPractisedAt = practisedAt ?? api.Clock.GetUtcNow() });
        }

        foreach (var (text, at) in wrongAnswers ?? [])
        {
            db.Attempts.Add(new Attempt { LearnerId = learner, VocabularyWordId = word.Id, AnswerText = text, IsDontKnow = text is null, IsCorrect = false, LevelAfter = level ?? MasteryLevel.Learning, AnsweredAt = at });
        }

        for (var i = 0; i < rightAnswers; i++)
        {
            db.Attempts.Add(new Attempt { LearnerId = learner, VocabularyWordId = word.Id, AnswerText = english, IsCorrect = true, LevelAfter = level ?? MasteryLevel.Learning, AnsweredAt = api.Clock.GetUtcNow().AddDays(-1) });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A game, in a world of its own when <paramref name="world"/> names one; returns that world's ID.</summary>
    private static async Task<Guid?> GameAsync(ApiHost api, Guid learner, SessionStatus status, DateTimeOffset startedAt, DateTimeOffset? completedAt, (string English, string? Typed, bool Correct, DateTimeOffset At)? answer = null, SessionKind kind = SessionKind.Game, string? world = null)
    {
        await using var db = api.Database.Context();
        Guid? worldId = null;
        if (world is not null)
        {
            var row = new World { Id = Guid.CreateVersion7(), LearnerId = learner, Name = world, Status = WorldStatus.Ready };
            db.Worlds.Add(row);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            worldId = row.Id;
        }

        var game = new PracticeSession { Id = Guid.CreateVersion7(), LearnerId = learner, Kind = kind, WorldId = worldId, Status = status, StartedAt = startedAt, CompletedAt = completedAt };
        db.PracticeSessions.Add(game);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (answer is { } given)
        {
            var wordId = db.VocabularyWords.Where(word => word.LearnerId == learner && word.English == given.English).Select(word => word.Id).Single();
            db.Attempts.Add(new Attempt { LearnerId = learner, VocabularyWordId = wordId, SessionId = game.Id, AnswerText = given.Typed, IsDontKnow = given.Typed is null, IsCorrect = given.Correct, LevelAfter = MasteryLevel.Learning, AnsweredAt = given.At });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return worldId;
    }

    private static string BangkokDay(DateTimeOffset instant) => instant.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task ManyWordsAsync(ApiHost api, Guid learner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await WordAsync(api, learner, $"word{i}", MasteryLevel.Learning, wrongAnswers: [($"wrong{i}", api.Clock.GetUtcNow().AddMinutes(-i))]);
        }
    }

    private static Task<object> RunAsync(ApiHost api, Guid learner, string tool, object input) =>
        RunAsync(api, learner, tool, JsonSerializer.Serialize(input));

    private static async Task<object> RunAsync(ApiHost api, Guid learner, string tool, string input)
    {
        object result = null!;
        await WithToolsAsync(api, learner, async tools =>
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input)!;
            result = await tools.Single(found => found.Name == tool).Run(arguments, TestContext.Current.CancellationToken);
        });
        return result;
    }

    private static async Task WithToolsAsync(ApiHost api, Guid learner, Func<IReadOnlyList<TutorTool>, Task> use)
    {
        await using var scope = api.Service<IServiceScopeFactory>().CreateAsyncScope();
        await use(scope.ServiceProvider.GetRequiredService<TutorTools>().ForLearner(learner));
    }

    [GeneratedRegex("learner|user|account", RegexOptions.IgnoreCase)]
    private static partial Regex LearnerWords();
}
