using LanguZe.Api.Modules.Learning.Domain;

namespace LanguZe.Api.Tests.Learning;

/// <summary>mastery-rules.spec.ts: SRS 4.1, one attempt at a time.</summary>
public sealed class MasteryRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Fact]
    public void A_new_word_starts_learning_with_a_streak_when_the_first_answer_is_right() =>
        Assert.Equal(new Mastery(Stage.Learning, 1, null), MasteryRules.Next(null, correct: true, Today));

    [Fact]
    public void A_new_word_starts_learning_with_nothing_when_the_first_answer_is_wrong() =>
        Assert.Equal(new Mastery(Stage.Learning, 0, null), MasteryRules.Next(null, correct: false, Today));

    [Fact]
    public void A_learning_word_builds_a_streak() =>
        Assert.Equal(new Mastery(Stage.Learning, 1, null), MasteryRules.Next(new Mastery(Stage.Learning, 0, null), correct: true, Today));

    [Fact]
    public void A_learning_word_becomes_familiar_on_the_second_right_answer_and_today_is_its_day() =>
        Assert.Equal(new Mastery(Stage.Familiar, 0, Today), MasteryRules.Next(new Mastery(Stage.Learning, 1, null), correct: true, Today));

    [Fact]
    public void A_learning_word_loses_the_streak_on_a_wrong_answer_but_not_the_level() =>
        Assert.Equal(new Mastery(Stage.Learning, 0, Yesterday), MasteryRules.Next(new Mastery(Stage.Learning, 1, Yesterday), correct: false, Today));

    // SRS 4.1, US-040 #4: a right answer on the day it became FAMILIAR says nothing about tomorrow.
    [Fact]
    public void A_familiar_word_does_not_move_on_its_own_familiar_day()
    {
        var familiar = new Mastery(Stage.Familiar, 0, Today);

        Assert.Equal(familiar, MasteryRules.Next(familiar, correct: true, Today));
    }

    [Fact]
    public void A_familiar_word_builds_a_streak_on_a_later_day() =>
        Assert.Equal(new Mastery(Stage.Familiar, 1, Yesterday), MasteryRules.Next(new Mastery(Stage.Familiar, 0, Yesterday), correct: true, Today));

    [Fact]
    public void A_familiar_word_becomes_mastered_on_the_second_right_answer_after_its_day() =>
        Assert.Equal(new Mastery(Stage.Mastered, 0, Yesterday), MasteryRules.Next(new Mastery(Stage.Familiar, 1, Yesterday), correct: true, Today));

    [Fact]
    public void A_familiar_word_cannot_be_mastered_twice_over_in_one_day()
    {
        var started = new Mastery(Stage.Familiar, 1, Today);

        Assert.Equal(started, MasteryRules.Next(started, correct: true, Today));
    }

    [Fact]
    public void A_familiar_word_falls_back_to_learning_on_a_wrong_answer() =>
        Assert.Equal(new Mastery(Stage.Learning, 0, Yesterday), MasteryRules.Next(new Mastery(Stage.Familiar, 1, Yesterday), correct: false, Today));

    [Fact]
    public void A_mastered_word_stays_as_it_is_while_the_answers_are_right()
    {
        var mastered = new Mastery(Stage.Mastered, 0, Yesterday);

        Assert.Equal(mastered, MasteryRules.Next(mastered, correct: true, Today));
    }

    [Fact]
    public void A_mastered_word_falls_back_to_familiar_on_a_wrong_answer_with_today_as_its_day() =>
        Assert.Equal(new Mastery(Stage.Familiar, 0, Today), MasteryRules.Next(new Mastery(Stage.Mastered, 0, Yesterday), correct: false, Today));

    // US-040: four right answers across two days, and no fewer.
    [Fact]
    public void The_whole_way_up_takes_four_right_answers_across_two_days()
    {
        var mastery = MasteryRules.Next(null, correct: true, Yesterday);
        mastery = MasteryRules.Next(mastery, correct: true, Yesterday);
        Assert.Equal(Stage.Familiar, mastery.Level);
        mastery = MasteryRules.Next(mastery, correct: true, Yesterday);
        Assert.Equal(Stage.Familiar, mastery.Level);

        mastery = MasteryRules.Next(mastery, correct: true, Today);
        mastery = MasteryRules.Next(mastery, correct: true, Today);

        Assert.Equal(Stage.Mastered, mastery.Level);
    }

    [Fact]
    public void A_streak_is_never_negative()
    {
        Mastery? mastery = null;
        for (var answer = 0; answer < 12; answer++)
        {
            mastery = MasteryRules.Next(mastery, correct: answer % 3 == 0, Today.AddDays(answer));
            Assert.True(mastery.Streak >= 0);
        }
    }
}
