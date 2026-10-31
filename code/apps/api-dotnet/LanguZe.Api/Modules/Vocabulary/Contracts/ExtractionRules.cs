using System.Text.RegularExpressions;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Platform.Text;

namespace LanguZe.Api.Modules.Vocabulary.Contracts;

/// <summary>A highlight box on the prepared photo, in 0–1 coordinates (AIR-003).</summary>
public sealed record WordBox(double X, double Y, double Width, double Height);

/// <summary>What an AI provider offers for one object it found in a photo: untrusted until checked.</summary>
public sealed record ExtractedItem(string English, string ThaiMeaning, string ExampleSentence, string CefrLevel, IReadOnlyList<string> AcceptedVariants, WordBox Box);

/// <summary>One item that passed every rule and is ready to be saved.</summary>
public sealed record CheckedItem(string English, string ThaiMeaning, string ThaiMeaningKey, string ExampleSentence, CefrLevel CefrLevel, IReadOnlyList<string> AcceptedVariants, WordBox Box);

/// <summary>
/// What an extracted item has to be before it is saved (extraction-rules.ts; AIR-003,
/// AIR-004, AIR-009, FR-023, FR-024): the part of the AI pipeline that holds whatever the
/// model returns. The vocabulary module holds rules only, so these are its contract.
/// </summary>
public static partial class ExtractionRules
{
    /// <summary>A world keeps at most this many words, the clearest objects first (FR-024).</summary>
    public const int MaxWordsPerWorld = 12;

    /// <summary>Below this, the photo is not worth learning from and the analysis fails (FR-024).</summary>
    public const int MinWordsPerWorld = 3;

    private const int MaxEnglishLength = 40;
    private const int MaxThaiLength = 100;
    private const int MaxSentenceLength = 200;
    private const int MaxVariants = 10;

    /// <summary>
    /// Words for people and parts of the body, which extraction must never return (FR-094,
    /// AIR-009). The prompt says so as well; this is the part that holds when the model
    /// ignores it, because a photo with a person in it is allowed and only these words are not.
    /// </summary>
    private static readonly HashSet<string> PeopleAndBody = new(StringComparer.Ordinal)
    {
        "man", "woman", "boy", "girl", "child", "baby", "person", "people", "lady", "gentleman",
        "face", "head", "hair", "eye", "eyebrow", "ear", "nose", "mouth", "lip", "tooth",
        "tongue", "chin", "cheek", "neck", "shoulder", "arm", "elbow", "hand", "finger", "thumb",
        "chest", "stomach", "back", "waist", "hip", "leg", "knee", "foot", "toe", "skin", "body",
    };

    private static readonly Dictionary<string, CefrLevel> CefrLevels = new(StringComparer.Ordinal)
    {
        ["A1"] = CefrLevel.A1,
        ["A2"] = CefrLevel.A2,
        ["B1"] = CefrLevel.B1,
        ["B2"] = CefrLevel.B2,
        ["C1"] = CefrLevel.C1,
        ["C2"] = CefrLevel.C2,
    };

    /// <summary>
    /// The normalization of SRS 4.2, for comparing answers and for deciding when two words
    /// are the same: spaces collapsed, lower case, one leading article removed.
    /// </summary>
    public static string NormalizeAnswer(string value)
    {
        var collapsed = JsText.CollapseWhiteSpace(JsText.Trim(value)).ToLowerInvariant();
        return LeadingArticle().Replace(collapsed, string.Empty, 1);
    }

    /// <summary>Two spellings of one Thai meaning are one word, so identity ignores spacing.</summary>
    public static string ThaiMeaningKeyOf(string meaning) => JsText.CollapseWhiteSpace(JsText.Trim(meaning));

    /// <summary>
    /// Whether one item may be saved (AIR-003, AIR-009). An item that fails is dropped and
    /// the rest of the response is kept (FR-023): one bad object out of eight is not a
    /// reason to lose the photo's other words.
    /// </summary>
    public static CheckedItem? CheckItem(ExtractedItem item)
    {
        var english = NormalizeAnswer(item.English);
        if (english.Length is 0 or > MaxEnglishLength || !EnglishWord().IsMatch(english) || PeopleAndBody.Contains(english))
        {
            return null;
        }

        var thaiMeaning = JsText.Trim(item.ThaiMeaning);
        if (thaiMeaning.Length is 0 or > MaxThaiLength || !ThaiScript().IsMatch(thaiMeaning))
        {
            return null;
        }

        var exampleSentence = JsText.Trim(item.ExampleSentence);
        if (exampleSentence.Length is 0 or > MaxSentenceLength)
        {
            return null;
        }

        var variants = AcceptedVariantsOf(item.AcceptedVariants, english);
        // The sentence has to teach the word, so it must contain it or one of its variants.
        if (!Mentions(exampleSentence, [english, .. variants]))
        {
            return null;
        }

        if (!CefrLevels.TryGetValue(item.CefrLevel, out var level) || !IsInsidePhoto(item.Box))
        {
            return null;
        }

        return new CheckedItem(english, thaiMeaning, ThaiMeaningKeyOf(thaiMeaning), exampleSentence, level, variants, item.Box);
    }

    /// <summary>
    /// The items to save, in the provider's own order of confidence: each one checked,
    /// duplicates of the same word merged so one box remains, and at most twelve (AIR-004, FR-024).
    /// </summary>
    public static IReadOnlyList<CheckedItem> CheckedItems(IEnumerable<ExtractedItem> items)
    {
        var kept = new List<CheckedItem>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (CheckItem(item) is not { } checkedItem)
            {
                continue;
            }

            // The first box wins: the provider lists what it is surest of first.
            if (identities.Add($"{checkedItem.English}|{checkedItem.ThaiMeaningKey}"))
            {
                kept.Add(checkedItem);
            }

            if (kept.Count == MaxWordsPerWorld)
            {
                break;
            }
        }

        return kept;
    }

    /// <summary>Normalized, unique, never the word itself, and at most ten (AIR-003, V19).</summary>
    private static List<string> AcceptedVariantsOf(IEnumerable<string> variants, string english)
    {
        var kept = new List<string>();
        foreach (var variant in variants)
        {
            var normalized = NormalizeAnswer(variant);
            if (normalized.Length == 0 || normalized == english || !EnglishWord().IsMatch(normalized) || normalized.Length > MaxEnglishLength)
            {
                continue;
            }

            if (!kept.Contains(normalized, StringComparer.Ordinal))
            {
                kept.Add(normalized);
            }

            if (kept.Count == MaxVariants)
            {
                break;
            }
        }

        return kept;
    }

    /// <summary>
    /// A word and its regular plurals: sofa, sofas; box, boxes; battery, batteries. In a
    /// phrase only the last word takes the ending. An irregular plural is not guessed.
    /// </summary>
    private static IEnumerable<string> FormsOf(string word)
    {
        yield return word;
        yield return $"{word}s";
        yield return $"{word}es";
        if (EndsInConsonantY().IsMatch(word))
        {
            yield return $"{word[..^1]}ies";
        }
    }

    /// <summary>Whether the sentence uses one of these words, rather than merely containing the letters: "car" is not in "carpet".</summary>
    private static bool Mentions(string sentence, IEnumerable<string> words)
    {
        var normalized = $" {JsText.Trim(Spaces().Replace(NotLetters().Replace(sentence.ToLowerInvariant(), " "), " "))} ";
        return words.SelectMany(FormsOf).Any(form => normalized.Contains($" {form} ", StringComparison.Ordinal));
    }

    /// <summary>A box has a real size and lies inside the photo, in 0–1 coordinates (AIR-003).</summary>
    private static bool IsInsidePhoto(WordBox box) =>
        new[] { box.X, box.Y, box.Width, box.Height }.All(double.IsFinite)
        && box.Width > 0 && box.Height > 0
        && box.X >= 0 && box.Y >= 0 && box.X + box.Width <= 1 && box.Y + box.Height <= 1;

    [GeneratedRegex("^(?:a|an|the) ")]
    private static partial Regex LeadingArticle();

    /// <summary>Letters, spaces and hyphens only: "coffee table" and "T-shirt" pass, "sofa!" does not.</summary>
    [GeneratedRegex(@"^[a-z]+(?:[ -][a-z]+)*\z")]
    private static partial Regex EnglishWord();

    [GeneratedRegex(@"[\u0E00-\u0E7F]")]
    private static partial Regex ThaiScript();

    [GeneratedRegex("[^aeiou]y\\z")]
    private static partial Regex EndsInConsonantY();

    [GeneratedRegex("[^a-z -]+")]
    private static partial Regex NotLetters();

    [GeneratedRegex($"[{JsText.WhiteSpace}]+")]
    private static partial Regex Spaces();
}
