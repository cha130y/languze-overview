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
            Assert.Equal(["weak_words", "recent_mistakes", "latest_game", "word_details"], tools.Select(tool => tool.Name));
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

    [Fact]
    public async Task Latest_game_reads_only_the_last_completed_game()
    {
        await using var api = await ApiHost.StartAsync();
        var (_, learner) = await Learners.VerifiedLearnerAsync(api, "tools-game@example.com");
        var now = api.Clock.GetUtcNow();
        await WordAsync(api, learner, "cloud", MasteryLevel.Learning, wrongAnswers: [("sky", now)]);
        async Task<LatestGameResult> ReadAsync() =>
            Assert.IsType<LatestGameResult>(await RunAsync(api, learner, "latest_game", new { }));

        Assert.False((await ReadAsync()).HasCompletedGame);
        await using var db = api.Database.Context();
        var game = new PracticeSession { Id = Guid.CreateVersion7(), LearnerId = learner, Kind = SessionKind.Game, Status = SessionStatus.Completed, StartedAt = now.AddMinutes(-3), CompletedAt = now };
        db.PracticeSessions.Add(game);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Attempts.Single(attempt => attempt.LearnerId == learner).SessionId = game.Id;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var review = new PracticeSession { LearnerId = learner, Kind = SessionKind.Review, Status = SessionStatus.Completed, StartedAt = now, CompletedAt = now.AddMinutes(1) };
        db.PracticeSessions.Add(review);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal("sky", Assert.Single((await ReadAsync()).Mistakes).Answered);

        db.PracticeSessions.Add(new PracticeSession { LearnerId = learner, Kind = SessionKind.Game, Status = SessionStatus.Completed, StartedAt = now, CompletedAt = now.AddMinutes(2) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var last = await ReadAsync();
        Assert.True(last.HasCompletedGame);
        Assert.Empty(last.Mistakes);
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
