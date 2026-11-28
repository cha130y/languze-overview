using System.Globalization;
using System.Text.Json;
using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Vocabulary.Contracts;
using LanguZe.Api.Platform.Text;
using LanguZe.Api.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Tutor.Shared;

/// <summary>
/// What the tutor may look up (tutor-tools.service.ts; FR-072, AI tutor flow section 2).
/// Every tool is built for one learner and closes over their ID, so none of them takes a
/// learner identifier and the model has no way to name one: that is the whole of the
/// authorization. Nothing here writes. What comes back is data about the learner, including
/// text they typed themselves, which travels to the model as a tool result, quoted data and
/// never instructions (AIR-002).
/// </summary>
internal sealed class TutorTools(LanguZeDbContext db)
{
    /// <summary>The most rows any tool will hand back, however many the model asks for.</summary>
    public const int MostRows = 20;

    private const int DefaultRows = 10;

    /// <summary>The tools, bound to this learner and to nobody else.</summary>
    public IReadOnlyList<TutorTool> ForLearner(Guid learnerId) =>
    [
        new(
            "weak_words",
            "The learner's words that are not yet mastered, the least known first, with how often they have got each one wrong.",
            [new("count", "integer", $"How many words to return, 1 to {MostRows}.")],
            async (input, cancellationToken) => await WeakWordsAsync(learnerId, RowsFrom(input), cancellationToken)),
        new(
            "recent_mistakes",
            "The answers the learner has recently got wrong, newest first, with what they typed.",
            [new("count", "integer", $"How many mistakes to return, 1 to {MostRows}.")],
            async (input, cancellationToken) => await RecentMistakesAsync(learnerId, RowsFrom(input), cancellationToken)),
        new(
            "latest_game",
            "Mistakes from the learner's most recently played game, finished or not. When that game has none, earlier games are searched, newest first, until one has.",
            [],
            async (_, cancellationToken) => await LatestGameAsync(learnerId, finishedOnly: false, cancellationToken)),
        new(
            "latest_finished_game",
            "Like latest_game, but only games the learner finished. Only for a question that asks about a finished game.",
            [],
            async (_, cancellationToken) => await LatestGameAsync(learnerId, finishedOnly: true, cancellationToken)),
        new(
            "word_details",
            "One of the learner's own words: its meaning, an example, and how they have answered it.",
            [new("word", "string", "The English word to look up.")],
            (input, cancellationToken) => WordDetailsAsync(learnerId, input.GetValueOrDefault("word"), cancellationToken)),
    ];

    /// <summary>
    /// A count the model asked for, made safe. Anything unusable becomes the default rather
    /// than an error: the model guessing badly at a number is not a reason to refuse the learner
    /// an answer. Only a JSON number counts, rounded down as <c>Math.floor</c> does.
    /// </summary>
    private static int RowsFrom(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (!input.TryGetValue("count", out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            return DefaultRows;
        }

        var asked = Math.Floor(number);
        return asked < 1 ? DefaultRows : (int)Math.Min(asked, MostRows);
    }

    /// <summary>Words still being learned, least known first (FR-072).</summary>
    private async Task<WeakWordsResult> WeakWordsAsync(Guid learnerId, int count, CancellationToken cancellationToken)
    {
        var words = await (
            from mastery in db.WordMastery
            join word in db.VocabularyWords on mastery.VocabularyWordId equals word.Id
            where mastery.LearnerId == learnerId && (mastery.Level == MasteryLevel.Learning || mastery.Level == MasteryLevel.Familiar)
            orderby mastery.Level, mastery.LastPractisedAt
            select new
            {
                word.English,
                word.ThaiMeaning,
                mastery.Level,
                Mistakes = db.Attempts.Count(attempt => attempt.VocabularyWordId == word.Id && !attempt.IsCorrect),
                mastery.LastPractisedAt,
            })
            .Take(count)
            .ToListAsync(cancellationToken);

        return new WeakWordsResult([.. words.Select(row => new WeakWord(row.English, row.ThaiMeaning, LevelName(row.Level), row.Mistakes, row.LastPractisedAt))]);
    }

    /// <summary>
    /// The mistakes of "the latest game" as the learner means it (decided 2026-11-28): the game
    /// played most recently, finished or left unfinished, ordered by its last answer; when it
    /// has no mistakes, the game before it, and so on. Asked about a finished game, only
    /// finished games count, ordered by when they finished. Review sessions are not games.
    /// The result says how many newer games had no mistakes, so the tutor can say the mistakes
    /// come from an earlier game.
    /// </summary>
    private async Task<LatestGameResult> LatestGameAsync(Guid learnerId, bool finishedOnly, CancellationToken cancellationToken)
    {
        var games = db.PracticeSessions
            .Where(session => session.LearnerId == learnerId && session.Kind == SessionKind.Game && (!finishedOnly || session.Status == SessionStatus.Completed))
            .Select(session => new
            {
                session.Id,
                session.Status,
                PlayedAt = db.Attempts.Where(attempt => attempt.SessionId == session.Id).Max(attempt => (DateTimeOffset?)attempt.AnsweredAt) ?? session.StartedAt,
                session.CompletedAt,
                World = db.Worlds.Where(world => world.Id == session.WorldId).Select(world => world.Name).FirstOrDefault(),
                HasMistakes = db.Attempts.Any(attempt => attempt.SessionId == session.Id && !attempt.IsCorrect),
            });
        var ordered = finishedOnly
            ? games.OrderByDescending(game => game.CompletedAt).ThenByDescending(game => game.Id)
            : games.OrderByDescending(game => game.PlayedAt).ThenByDescending(game => game.Id);
        var found = await ordered.Select(game => new { game.Id, game.Status, game.PlayedAt, game.World, game.HasMistakes }).ToListAsync(cancellationToken);

        // The day in Bangkok, as the learner counts days: read as a UTC instant, a game played
        // between midnight and 7 am in Bangkok falls on the day before, so the model is handed the date.
        static string DayOf(DateTimeOffset instant) => instant.ToOffset(BangkokDay.Offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (found.Count == 0)
        {
            return new LatestGameResult(false, null, null, 0);
        }

        // Each game in a box of its own, named for what it is. One flat object with a world and a
        // date beside a nested latest game let the model give the earlier game's mistakes the
        // latest game's world (2026-11-28, 3 replies of 3).
        async Task<PlayedGame> BoxAsync(int index)
        {
            var game = found[index];
            var mistakes = game.HasMistakes ? (await RecentMistakesAsync(learnerId, MostRows, cancellationToken, game.Id)).Mistakes : [];
            return new PlayedGame(game.World, DayOf(game.PlayedAt), game.Status == SessionStatus.Completed, mistakes);
        }

        var latest = await BoxAsync(0);
        var withMistakes = found.FindIndex(game => game.HasMistakes);
        if (withMistakes <= 0)
        {
            return new LatestGameResult(true, latest, null, 0);
        }

        return new LatestGameResult(true, latest, await BoxAsync(withMistakes), withMistakes - 1);
    }

    /// <summary>What the learner recently got wrong, and what they actually typed (FR-042).</summary>
    private async Task<RecentMistakesResult> RecentMistakesAsync(Guid learnerId, int count, CancellationToken cancellationToken, Guid? sessionId = null)
    {
        var mistakes = await (
            from attempt in db.Attempts
            join word in db.VocabularyWords on attempt.VocabularyWordId equals word.Id
            join asked in db.WordOccurrences on attempt.OccurrenceId equals asked.Id into askedIn
            from asked in askedIn.DefaultIfEmpty()
            where attempt.LearnerId == learnerId && !attempt.IsCorrect && (sessionId == null || attempt.SessionId == sessionId)
            orderby attempt.AnsweredAt descending
            // The meaning in the photo it was asked in, or the word's own once that occurrence is gone (FR-043).
            select new { word.English, ThaiMeaning = asked != null ? asked.ThaiMeaning : word.ThaiMeaning, attempt.IsDontKnow, attempt.AnswerText, attempt.AnsweredAt })
            .Take(count)
            .ToListAsync(cancellationToken);

        // "I don't know" is not a wrong answer, so it is reported as its own thing.
        return new RecentMistakesResult([.. mistakes.Select(row => new Mistake(row.English, row.ThaiMeaning, row.IsDontKnow ? null : row.AnswerText, row.IsDontKnow, row.AnsweredAt))]);
    }

    /// <summary>
    /// One word of the learner's. The name is matched the way answers are (SRS 4.2), so a model
    /// writing "the sofa" finds the word the learner knows as <c>sofa</c>.
    /// </summary>
    private async Task<object> WordDetailsAsync(Guid learnerId, JsonElement asked, CancellationToken cancellationToken)
    {
        if (asked.ValueKind != JsonValueKind.String || JsText.Trim(asked.GetString()!).Length == 0)
        {
            return new ToolRefusal("Give the English word to look up.");
        }

        var english = ExtractionRules.NormalizeAnswer(asked.GetString()!);
        var word = await db.VocabularyWords.Include(found => found.Mastery)
            .FirstOrDefaultAsync(found => found.LearnerId == learnerId && found.English == english, cancellationToken);

        // A word the learner does not have is not an error to hide: saying so is what stops the
        // tutor inventing a history for it (FR-075, US-071 criterion 3).
        if (word is null)
        {
            return new WordNotFound(false, "The learner does not have this word.");
        }

        var occurrence = await db.WordOccurrences.Where(found => found.VocabularyWordId == word.Id)
            .OrderByDescending(found => found.CreatedAt)
            .Select(found => new { found.ExampleSentence, found.CefrLevel })
            .FirstOrDefaultAsync(cancellationToken);
        var attempts = await db.Attempts.Where(attempt => attempt.VocabularyWordId == word.Id)
            .OrderByDescending(attempt => attempt.AnsweredAt)
            .Take(5)
            .Select(attempt => new { attempt.IsCorrect, attempt.IsDontKnow, attempt.AnswerText, attempt.AnsweredAt })
            .ToListAsync(cancellationToken);

        return new WordDetails(
            true,
            word.English,
            word.ThaiMeaning,
            occurrence?.ExampleSentence,
            occurrence is null ? null : occurrence.CefrLevel.ToString(),
            word.Mastery is null ? "NEW" : LevelName(word.Mastery.Level),
            [.. attempts.Select(attempt => new RecentAnswer(attempt.IsCorrect, attempt.IsDontKnow ? null : attempt.AnswerText, attempt.IsDontKnow, attempt.AnsweredAt))]);
    }

    private static string LevelName(MasteryLevel level) => level.ToString().ToUpperInvariant();
}

/// <summary>What a tool says when the model asks for something it cannot have.</summary>
internal sealed record ToolRefusal(string Error);

internal sealed record WeakWordsResult(IReadOnlyList<WeakWord> Words);

internal sealed record WeakWord(string English, string ThaiMeaning, string Mastery, int Mistakes, DateTimeOffset LastPractisedAt);

/// <param name="HasGame">The learner has played a game of the kind asked for.</param>
/// <param name="LatestGame">The game the learner means: played last, or finished last; with its own mistakes, if any.</param>
/// <param name="EarlierGameWithMistakes">Only when the latest game has no mistakes: the newest earlier game that has some.</param>
/// <param name="GamesBetween">Games played between those two, none of them with a mistake.</param>
internal sealed record LatestGameResult(bool HasGame, PlayedGame? LatestGame, PlayedGame? EarlierGameWithMistakes, int GamesBetween);

/// <summary>One game, named for the learner.</summary>
/// <param name="World">The name the learner gave the game's world, so they know which photo it was; null once the world is deleted. Text the learner typed, handed to the model as data (AIR-002).</param>
/// <param name="PlayedOn">The Bangkok date the game was last answered in, as yyyy-MM-dd.</param>
internal sealed record PlayedGame(string? World, string PlayedOn, bool Finished, IReadOnlyList<Mistake> Mistakes);

internal sealed record RecentMistakesResult(IReadOnlyList<Mistake> Mistakes);

/// <param name="Answered">What the learner typed; empty when they said they did not know.</param>
internal sealed record Mistake(string English, string ThaiMeaning, string? Answered, bool SaidTheyDidNotKnow, DateTimeOffset AnsweredAt);

internal sealed record WordNotFound(bool Found, string Error);

internal sealed record WordDetails(bool Found, string English, string ThaiMeaning, string? ExampleSentence, string? CefrLevel, string Mastery, IReadOnlyList<RecentAnswer> RecentAnswers);

internal sealed record RecentAnswer(bool Correct, string? Answered, bool SaidTheyDidNotKnow, DateTimeOffset AnsweredAt);
