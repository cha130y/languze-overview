using LanguZe.Api.Modules.Learning.Domain;

namespace LanguZe.Api.Tests.Learning;

/// <summary>question-selection.spec.ts: choosing a game's questions (FR-030).</summary>
public sealed class QuestionSelectionTests
{
    /// <summary>No shuffle at all, so a test can say exactly what comes out.</summary>
    private static readonly Func<IReadOnlyList<Candidate>, IReadOnlyList<Candidate>> AsGiven = items => items;

    [Fact]
    public void The_least_known_words_come_first()
    {
        var mastered = Word(Stage.Mastered);
        var familiar = Word(Stage.Familiar);
        var learning = Word(Stage.Learning);

        var chosen = QuestionSelection.Select([mastered, familiar, learning], AsGiven);

        Assert.Equal([learning, familiar, mastered], chosen);
    }

    [Fact]
    public void A_new_word_and_a_learning_word_are_equally_unknown()
    {
        var learning = Word(Stage.Learning);
        var fresh = Word(null);

        Assert.Equal([learning, fresh], QuestionSelection.Select([learning, fresh], AsGiven));
    }

    [Fact]
    public void At_most_ten_are_asked() =>
        Assert.Equal(QuestionSelection.QuestionsPerSession, QuestionSelection.Select([.. Enumerable.Range(0, 12).Select(_ => Word(null))]).Count);

    [Fact]
    public void All_of_a_small_world_is_asked() =>
        Assert.Equal(4, QuestionSelection.Select([.. Enumerable.Range(0, 4).Select(_ => Word(null))]).Count);

    [Fact]
    public void Nothing_is_asked_when_there_is_nothing() =>
        Assert.Empty(QuestionSelection.Select([]));

    // Random order keeps a world from becoming a sequence to memorize.
    [Fact]
    public void The_order_is_really_shuffled_and_nobody_is_lost()
    {
        Candidate[] ten = [.. Enumerable.Range(0, 10).Select(_ => Word(null))];
        var orders = new HashSet<string>();
        for (var round = 0; round < 50; round++)
        {
            var chosen = QuestionSelection.Select(ten);
            Assert.Equal(ten.Select(word => word.OccurrenceId).Order(), chosen.Select(word => word.OccurrenceId).Order());
            orders.Add(string.Join(',', chosen.Select(word => word.OccurrenceId)));
        }

        Assert.True(orders.Count > 1);
    }

    private static Candidate Word(Stage? level) => new(Guid.NewGuid(), Guid.NewGuid(), level);
}
