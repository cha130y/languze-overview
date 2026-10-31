using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Vocabulary.Contracts;

namespace LanguZe.Api.Tests.Vocabulary;

/// <summary>extraction-rules.spec.ts: what an extracted item has to be before it is saved (AIR-003, AIR-004, FR-023, FR-024).</summary>
public sealed class ExtractionRulesTests
{
    private static readonly WordBox SofaBox = new(0.1, 0.2, 0.3, 0.25);

    [Theory]
    [InlineData("Sofa", "sofa")]
    [InlineData("  a   sofa ", "sofa")]
    [InlineData("The Coffee  Table", "coffee table")]
    [InlineData("an apple", "apple")]
    // Only a leading article goes; "the" inside the phrase is part of it.
    [InlineData("end of the day", "end of the day")]
    // JavaScript's trim() takes U+FEFF, which .NET's Trim() leaves.
    [InlineData("\ufeffsofa\u3000", "sofa")]
    public void An_answer_is_normalized_as_srs_4_2_says(string given, string expected) =>
        Assert.Equal(expected, ExtractionRules.NormalizeAnswer(given));

    [Fact]
    public void A_thai_meaning_key_ignores_spacing_so_one_meaning_is_one_word() =>
        Assert.Equal("โซฟา ยาว", ExtractionRules.ThaiMeaningKeyOf("  โซฟา   ยาว "));

    [Fact]
    public void A_good_item_is_kept_normalized()
    {
        var checkedItem = ExtractionRules.CheckItem(Item(english: " A Sofa "));

        Assert.NotNull(checkedItem);
        Assert.Equal("sofa", checkedItem.English);
        Assert.Equal("โซฟา", checkedItem.ThaiMeaningKey);
        Assert.Equal(CefrLevel.A1, checkedItem.CefrLevel);
    }

    [Theory]
    [InlineData("   ", "โซฟา", "A1")]
    [InlineData("sofa 2", "โซฟา", "A1")]
    [InlineData("sofa!", "โซฟา", "A1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "โซฟา", "A1")]
    [InlineData("sofa", "sofa", "A1")]
    [InlineData("sofa", " ", "A1")]
    [InlineData("sofa", "โซฟา", "D1")]
    public void A_bad_word_meaning_or_level_is_refused(string english, string thaiMeaning, string level) =>
        Assert.Null(ExtractionRules.CheckItem(Item(english: english, thaiMeaning: thaiMeaning, cefrLevel: level)));

    [Fact]
    public void A_meaning_over_100_characters_is_refused() =>
        Assert.Null(ExtractionRules.CheckItem(Item(thaiMeaning: string.Concat(Enumerable.Repeat("โซ", 60)))));

    [Fact]
    public void A_sentence_over_200_characters_is_refused() =>
        Assert.Null(ExtractionRules.CheckItem(Item(exampleSentence: "sofa " + string.Concat(Enumerable.Repeat("x ", 120)))));

    // The sentence has to teach the word, which is the whole point of showing it.
    [Fact]
    public void A_sentence_that_does_not_use_the_word_is_refused() =>
        Assert.Null(ExtractionRules.CheckItem(Item(exampleSentence: "This room is very tidy.")));

    [Fact]
    public void A_sentence_that_uses_a_variant_instead_is_accepted() =>
        Assert.NotNull(ExtractionRules.CheckItem(Item(exampleSentence: "The couch is green.", acceptedVariants: ["couch"])));

    [Fact]
    public void Letters_inside_another_word_are_not_the_word()
    {
        Assert.Null(ExtractionRules.CheckItem(Item(english: "car", exampleSentence: "The carpet is red.")));
        Assert.Null(ExtractionRules.CheckItem(Item(exampleSentence: "We sat on the sofabed.")));
    }

    // A regular plural is the word: refusing "white tiles" for tile lost three words of eleven in the evaluation.
    [Theory]
    [InlineData("sofa", "I like sofas.")]
    [InlineData("box", "The boxes are full.")]
    [InlineData("battery", "The batteries are new.")]
    [InlineData("toy", "The toys are on the floor.")]
    [InlineData("coffee table", "We have two coffee tables.")]
    public void A_sentence_using_the_regular_plural_is_accepted(string english, string exampleSentence) =>
        Assert.NotNull(ExtractionRules.CheckItem(Item(english: english, exampleSentence: exampleSentence)));

    [Fact]
    public void An_irregular_plural_is_not_guessed_but_a_variant_can_give_it()
    {
        Assert.Null(ExtractionRules.CheckItem(Item(english: "knife", exampleSentence: "The knives are sharp.")));
        Assert.NotNull(ExtractionRules.CheckItem(Item(english: "knife", exampleSentence: "The knives are sharp.", acceptedVariants: ["knives"])));
    }

    // FR-094, AIR-009: a photo may contain a person; their words may not be taught.
    [Theory]
    [InlineData("man")]
    [InlineData("woman")]
    [InlineData("child")]
    [InlineData("hand")]
    [InlineData("face")]
    [InlineData("hair")]
    public void A_word_for_a_person_or_a_part_of_the_body_is_refused(string english) =>
        Assert.Null(ExtractionRules.CheckItem(Item(english: english, exampleSentence: $"This is a {english}.", acceptedVariants: [])));

    [Theory]
    [InlineData(-0.1, 0.1, 0.2, 0.2)]
    [InlineData(0.9, 0.1, 0.2, 0.2)]
    [InlineData(0.1, 0.95, 0.1, 0.2)]
    [InlineData(0.1, 0.1, 0, 0.2)]
    [InlineData(0.1, 0.1, 0.2, -0.2)]
    [InlineData(0.1, 0.1, double.NaN, 0.2)]
    public void A_box_outside_the_photo_or_without_a_size_is_refused(double x, double y, double width, double height) =>
        Assert.Null(ExtractionRules.CheckItem(Item(box: new WordBox(x, y, width, height))));

    [Fact]
    public void A_box_filling_the_whole_photo_is_accepted() =>
        Assert.NotNull(ExtractionRules.CheckItem(Item(box: new WordBox(0, 0, 1, 1))));

    [Fact]
    public void Variants_are_normalized_and_never_the_word_itself() =>
        Assert.Equal(["couch", "settee"], ExtractionRules.CheckItem(Item(acceptedVariants: ["Couch", " the sofa ", "settee"]))!.AcceptedVariants);

    [Fact]
    public void At_most_ten_variants_are_kept_without_duplicates()
    {
        var many = Enumerable.Range(0, 15).Select(index => $"couch{(char)('a' + index)}");

        var variants = ExtractionRules.CheckItem(Item(acceptedVariants: ["couch", "couch", .. many]))!.AcceptedVariants;

        Assert.Equal(10, variants.Count);
        Assert.Equal(10, variants.Distinct().Count());
    }

    [Fact]
    public void A_variant_that_is_not_a_word_is_dropped() =>
        Assert.Equal(["couch"], ExtractionRules.CheckItem(Item(acceptedVariants: ["couch", "sofa!!"]))!.AcceptedVariants);

    [Fact]
    public void A_response_keeps_its_good_items_and_drops_the_bad_ones()
    {
        var kept = ExtractionRules.CheckedItems([
            Item(),
            Item(english: "lamp!", thaiMeaning: "โคมไฟ"),
            Item(english: "table", thaiMeaning: "โต๊ะ", exampleSentence: "The table is round."),
        ]);

        Assert.Equal(["sofa", "table"], kept.Select(word => word.English));
    }

    // AIR-004: one word, one box; the first is what the model was surest of.
    [Fact]
    public void A_word_found_twice_keeps_its_first_box()
    {
        var kept = ExtractionRules.CheckedItems([Item(box: new WordBox(0.1, 0.1, 0.2, 0.2)), Item(box: new WordBox(0.5, 0.5, 0.2, 0.2))]);

        Assert.Equal(0.1, Assert.Single(kept).Box.X);
    }

    [Fact]
    public void The_same_word_with_another_meaning_is_a_word_of_its_own() =>
        Assert.Equal(2, ExtractionRules.CheckedItems([
            Item(english: "glass", thaiMeaning: "แก้วน้ำ", exampleSentence: "The glass is full."),
            Item(english: "glass", thaiMeaning: "กระจก", exampleSentence: "The glass is clean."),
        ]).Count);

    [Fact]
    public void At_most_twelve_words_are_kept()
    {
        var many = Enumerable.Range(0, 20).Select(index => (char)('a' + index)).Select(suffix =>
            Item(english: $"object{suffix}", thaiMeaning: $"สิ่งของ{suffix}", exampleSentence: $"This is object{suffix}.", acceptedVariants: []));

        Assert.Equal(ExtractionRules.MaxWordsPerWorld, ExtractionRules.CheckedItems(many).Count);
    }

    [Fact]
    public void A_response_of_only_bad_items_gives_nothing() =>
        Assert.Empty(ExtractionRules.CheckedItems([Item(english: "???"), Item(cefrLevel: "X")]));

    private static ExtractedItem Item(
        string english = "sofa",
        string thaiMeaning = "โซฟา",
        string exampleSentence = "I sit on the sofa.",
        string cefrLevel = "A1",
        string[]? acceptedVariants = null,
        WordBox? box = null) =>
        new(english, thaiMeaning, exampleSentence, cefrLevel, acceptedVariants ?? ["couch"], box ?? SofaBox);
}
