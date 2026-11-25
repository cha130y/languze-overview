using LanguZe.Api.Modules.Learning.Domain;

namespace LanguZe.Api.Tests.Learning;

/// <summary>session-summary.spec.ts: the end of a session (FR-035, US-033).</summary>
public sealed class SessionSummariesTests
{
    [Fact]
    public void The_right_answers_are_counted_and_the_xp_added_up()
    {
        var summary = SessionSummaries.Of([
            new(true, 10, null, Stage.Learning, "sofa"),
            new(false, 0, null, Stage.Learning, "rug"),
            new(true, 10, Stage.Learning, Stage.Learning, "clock"),
        ]);

        Assert.Equal((3, 2, 20), (summary.AnsweredCount, summary.CorrectCount, summary.XpEarned));
    }

    [Fact]
    public void The_words_that_moved_are_named_with_where_they_ended()
    {
        var summary = SessionSummaries.Of([
            new(true, 10, null, Stage.Learning, "sofa"),
            new(true, 10, Stage.Learning, Stage.Familiar, "rug"),
        ]);

        Assert.Equal([new LevelChange("sofa", Stage.Learning), new LevelChange("rug", Stage.Familiar)], summary.LevelChanges);
    }

    [Fact]
    public void Words_that_stayed_where_they_were_are_left_out()
    {
        var summary = SessionSummaries.Of([new(true, 10, Stage.Learning, Stage.Learning, "sofa"), new(true, 10, Stage.Mastered, Stage.Mastered, "rug")]);

        Assert.Empty(summary.LevelChanges);
    }

    [Fact]
    public void A_word_that_fell_back_is_named() =>
        Assert.Equal([new LevelChange("rug", Stage.Familiar)], SessionSummaries.Of([new(false, 0, Stage.Mastered, Stage.Familiar, "rug")]).LevelChanges);

    [Fact]
    public void An_empty_session_adds_up_to_nothing() =>
        Assert.Equal(new SessionSummary(0, 0, 0, []), SessionSummaries.Of([]), new SummaryComparer());

    private sealed class SummaryComparer : IEqualityComparer<SessionSummary>
    {
        public bool Equals(SessionSummary? x, SessionSummary? y) =>
            x is not null && y is not null && (x.AnsweredCount, x.CorrectCount, x.XpEarned) == (y.AnsweredCount, y.CorrectCount, y.XpEarned) && x.LevelChanges.SequenceEqual(y.LevelChanges);

        public int GetHashCode(SessionSummary obj) => obj.AnsweredCount;
    }
}
