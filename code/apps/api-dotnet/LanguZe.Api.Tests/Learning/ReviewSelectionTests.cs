using LanguZe.Api.Modules.Learning.Domain;

namespace LanguZe.Api.Tests.Learning;

/// <summary>review-selection.spec.ts: choosing what to review (FR-050).</summary>
public sealed class ReviewSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    // US-050 criterion 1, in the order it states.
    [Fact]
    public void Mistakes_come_first_then_familiar_then_learning() =>
        Assert.Equal(
            ["missed", "familiar", "learning"],
            NamesOf(ReviewSelection.Select([Word("learning"), Word("familiar", Stage.Familiar), Word("missed", Stage.Familiar, mistakeHoursAgo: 1)], 10)));

    // A word missed an hour ago is the one about to be forgotten.
    [Fact]
    public void The_most_recent_mistake_comes_first() =>
        Assert.Equal(
            ["fresh", "middling", "old"],
            NamesOf(ReviewSelection.Select([Word("old", mistakeHoursAgo: 40), Word("fresh", mistakeHoursAgo: 1), Word("middling", mistakeHoursAgo: 10)], 10)));

    // Among words never got wrong, the ones left alone longest come back first.
    [Fact]
    public void The_least_recently_practised_comes_first() =>
        Assert.Equal(
            ["last week", "yesterday", "just now"],
            NamesOf(ReviewSelection.Select([Word("yesterday", Stage.Familiar, practisedHoursAgo: 24), Word("just now", Stage.Familiar, practisedHoursAgo: 1), Word("last week", Stage.Familiar, practisedHoursAgo: 168)], 10)));

    // A missed FAMILIAR word comes before a LEARNING word that never was.
    [Fact]
    public void A_mistake_outranks_the_level() =>
        Assert.Equal(
            ["missed familiar", "clean learning"],
            NamesOf(ReviewSelection.Select([Word("clean learning"), Word("missed familiar", Stage.Familiar, mistakeHoursAgo: 1)], 10)));

    [Fact]
    public void A_word_is_asked_once_however_many_ways_it_qualifies() =>
        Assert.Equal(["one"], NamesOf(ReviewSelection.Select([Word("one", Stage.Familiar, mistakeHoursAgo: 1)], 10)));

    [Fact]
    public void It_stops_at_the_limit() =>
        Assert.Equal(10, ReviewSelection.Select([.. Enumerable.Range(0, 14).Select(i => Word($"w{i}", mistakeHoursAgo: i))], 10).Count);

    // US-050: review is for repairing what is half-known.
    [Fact]
    public void A_mastered_word_is_left_out() =>
        Assert.Equal(["learning"], NamesOf(ReviewSelection.Select([Word("mastered", Stage.Mastered, mistakeHoursAgo: 1), Word("learning")], 10)));

    [Fact]
    public void Nothing_is_asked_when_nothing_qualifies() => Assert.Empty(ReviewSelection.Select([], 10));

    // Unlike a game, a review is not shuffled: its order is the point (FR-030 against FR-050).
    [Fact]
    public void The_order_is_the_same_every_time()
    {
        ReviewCandidate[] candidates = [Word("a", mistakeHoursAgo: 2), Word("b", Stage.Familiar), Word("c")];

        var orders = Enumerable.Range(0, 5).Select(_ => string.Join(',', NamesOf(ReviewSelection.Select(candidates, 10)))).Distinct();

        Assert.Single(orders);
    }

    // As JavaScript's sort, which is stable: words that tie keep the order they came in.
    [Fact]
    public void Words_that_tie_keep_the_order_they_came_in() =>
        Assert.Equal(["first", "second"], NamesOf(ReviewSelection.Select([Word("first", Stage.Familiar), Word("second", Stage.Familiar)], 10)));

    /// <summary>A word named by <paramref name="name"/>, practised an hour ago unless told otherwise.</summary>
    private static ReviewCandidate Word(string name, Stage level = Stage.Learning, int practisedHoursAgo = 1, int? mistakeHoursAgo = null) => new(
        NamedGuid(name),
        Guid.NewGuid(),
        level,
        Now.AddHours(-practisedHoursAgo),
        mistakeHoursAgo is { } hours ? Now.AddHours(-hours) : null);

    private static readonly Dictionary<Guid, string> Names = [];

    private static Guid NamedGuid(string name)
    {
        var id = Guid.NewGuid();
        lock (Names)
        {
            Names[id] = name;
        }

        return id;
    }

    private static string[] NamesOf(IReadOnlyList<ReviewCandidate> chosen)
    {
        lock (Names)
        {
            return [.. chosen.Select(word => Names[word.VocabularyWordId])];
        }
    }
}
