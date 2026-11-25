using LanguZe.Api.Modules.Learning.Domain;

namespace LanguZe.Api.Tests.Learning;

/// <summary>answer-check.spec.ts: checking an answer (FR-032, SRS 4.2), and what it earns (FR-060).</summary>
public sealed class AnswerCheckTests
{
    private static readonly string[] SofaVariants = ["couch", "sofas"];

    // Code points, since the characters themselves would be invisible here too.
    private static readonly string SaraI = char.ConvertFromUtf32(0x0e34);
    private static readonly string MaiEk = char.ConvertFromUtf32(0x0e48);
    private static readonly string ZeroWidthSpace = char.ConvertFromUtf32(0x200b);
    private static readonly string SoftHyphen = char.ConvertFromUtf32(0x00ad);
    private static readonly string BoBaimai = char.ConvertFromUtf32(0x0e1a);

    // US-031 criterion 1, word for word.
    [Theory]
    [InlineData("sofa")]
    [InlineData("Sofa")]
    [InlineData("SOFA")]
    [InlineData(" a sofa ")]
    [InlineData("the sofa")]
    [InlineData("couch")]
    [InlineData("sofas")]
    public void The_word_and_its_variants_are_accepted_however_typed(string typed) => Assert.True(Sofa(typed));

    // A typo is not the word: accepting one would teach a spelling that is wrong.
    [Theory]
    [InlineData("sofaa")]
    [InlineData("sof")]
    [InlineData("so fa")]
    [InlineData("couche")]
    [InlineData("settee")]
    [InlineData("")]
    public void Anything_else_is_refused(string typed) => Assert.False(Sofa(typed));

    [Fact]
    public void Repeated_spaces_inside_an_answer_are_collapsed() =>
        Assert.True(AnswerCheck.IsCorrectAnswer("  coffee   table ", "coffee table", []));

    [Fact]
    public void Only_one_leading_article_comes_off() => Assert.False(Sofa("a a sofa"));

    [Fact]
    public void An_article_that_is_the_whole_answer_stays() => Assert.True(AnswerCheck.IsCorrectAnswer("the", "the", []));

    // SRS 4.2 and production, 2026-10-31: a Thai keyboard's b key typed sara i before "bag".
    [Fact]
    public void A_thai_vowel_mark_typed_before_switching_to_english_is_ignored() =>
        Assert.True(AnswerCheck.IsCorrectAnswer($"{SaraI}bag", "bag", []));

    [Fact]
    public void Other_characters_the_learner_cannot_see_are_ignored()
    {
        Assert.True(Sofa($"so{MaiEk}fa"));
        Assert.True(Sofa($"so{ZeroWidthSpace}fa"));
        Assert.True(Sofa($"{SoftHyphen}sofa"));
        Assert.True(Sofa($"{ZeroWidthSpace}the sofa"));
    }

    // A Thai letter is visible, so typing one is a mistake the learner can see.
    [Fact]
    public void A_thai_letter_the_learner_can_see_is_still_wrong() =>
        Assert.False(AnswerCheck.IsCorrectAnswer($"{BoBaimai}ag", "bag", []));

    [Fact]
    public void An_answer_of_nothing_but_invisible_characters_is_wrong()
    {
        Assert.False(AnswerCheck.IsCorrectAnswer(SaraI, "bag", []));
        Assert.False(AnswerCheck.IsCorrectAnswer($"{ZeroWidthSpace} ", "bag", []));
    }

    // FR-033: the feedback says when a right answer was a variant, not the word.
    [Theory]
    [InlineData("sofa", true)]
    [InlineData(" The SOFA ", true)]
    [InlineData("couch", false)]
    [InlineData("sofas", false)]
    [InlineData("sofaa", false)]
    public void The_word_is_told_from_its_variants(string typed, bool isTheWord) =>
        Assert.Equal(isTheWord, AnswerCheck.IsTheWord(typed, "sofa"));

    [Fact]
    public void A_stray_mark_does_not_turn_the_word_into_a_variant()
    {
        Assert.True(AnswerCheck.IsTheWord($"{SaraI}sofa", "sofa"));
        Assert.False(AnswerCheck.IsTheWord($"{SaraI}couch", "sofa"));
    }

    // US-031 criterion 3: a wrong answer earns nothing and never takes XP away.
    [Fact]
    public void A_right_answer_earns_ten_and_a_wrong_one_nothing() =>
        Assert.Equal((10, 0), (AnswerCheck.XpFor(true), AnswerCheck.XpFor(false)));

    private static bool Sofa(string typed) => AnswerCheck.IsCorrectAnswer(typed, "sofa", SofaVariants);
}
