using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.GenAI;
using Google.GenAI.Types;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Vocabulary.Contracts;
using LanguZe.Api.Platform.Config;
using LanguZe.Api.Platform.Text;

namespace LanguZe.Api.Modules.Ai.Shared.Gemini;

/// <summary>The part of the SDK this adapter uses, so a test can stand in for the network.</summary>
internal interface IGeminiModels
{
    Task<GenerateContentResponse> GenerateContentAsync(string model, List<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken);
}

/// <summary>The SDK's own client behind <see cref="IGeminiModels"/>.</summary>
internal sealed class GeminiModels(Client client) : IGeminiModels
{
    public Task<GenerateContentResponse> GenerateContentAsync(string model, List<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken) =>
        client.Models.GenerateContentAsync(model, contents, config, cancellationToken);
}

/// <summary>
/// Gemini behind LanguZe's AI interface (gemini-ai-provider.ts; ADR-0004). This is the only
/// code that knows the SDK: everything else depends on <see cref="IAiProvider"/>, so another
/// provider is another adapter rather than a change to the analysis. Nothing about the learner
/// is ever sent, only the prepared photo and fixed instructions (AIR-002, AIR-006). The tutor's
/// part arrives with the tutor slice.
/// <para>
/// Two of the C# SDK's defaults differ from the JavaScript one NestJS uses, and are set to
/// match it: one attempt, no retries, since no retry options are given; and no automatic
/// continuation, which the JavaScript SDK does not have.
/// </para>
/// </summary>
internal sealed partial class GeminiAiProvider(IGeminiModels models, GeminiSettings settings) : IAiProvider
{
    /// <summary>Gemini returns boxes as [ymin, xmin, ymax, xmax] from 0 to 1000 (ADR-0004).</summary>
    private const double BoxScale = 1000;

    /// <summary>Gemini's own words for why it refused, mapped to LanguZe's categories (FR-091).</summary>
    private static readonly Dictionary<string, BlockCategory> RefusalCategories = new(StringComparer.Ordinal)
    {
        ["HARM_CATEGORY_SEXUALLY_EXPLICIT"] = BlockCategory.SexualContent,
        ["HARM_CATEGORY_CSAM"] = BlockCategory.SuspectedIllegalMaterial,
        ["HARM_CATEGORY_DANGEROUS_CONTENT"] = BlockCategory.IllegalActivity,
        ["HARM_CATEGORY_HARASSMENT"] = BlockCategory.HateSymbol,
        ["HARM_CATEGORY_HATE_SPEECH"] = BlockCategory.HateSymbol,
    };

    /// <summary>The block categories by the names the prompt gives them.</summary>
    private static readonly Dictionary<string, BlockCategory> Categories = new(StringComparer.Ordinal)
    {
        ["SEXUAL_CONTENT"] = BlockCategory.SexualContent,
        ["SUSPECTED_ILLEGAL_MATERIAL"] = BlockCategory.SuspectedIllegalMaterial,
        ["VIOLENCE"] = BlockCategory.Violence,
        ["ILLEGAL_ACTIVITY"] = BlockCategory.IllegalActivity,
        ["HATE_SYMBOL"] = BlockCategory.HateSymbol,
        ["PERSONAL_DATA"] = BlockCategory.PersonalData,
        ["OTHER"] = BlockCategory.Other,
    };

    public string Name => "gemini";

    public string ModelFor(AiPurpose purpose) => purpose switch
    {
        AiPurpose.Extraction => settings.ExtractionModel,
        AiPurpose.Tutor => settings.TutorModel,
        _ => settings.SafetyModel,
    };

    /// <summary>
    /// Classifies the photo before anything describes it (FR-092). Gemini refusing to answer at
    /// all counts as a block, because a photo its own filters will not look at is not one
    /// LanguZe should keep (ADR-0004).
    /// </summary>
    public async Task<AiAnswer<SafetyVerdict>> CheckPhotoAsync(PhotoForAi photo, CancellationToken cancellationToken)
    {
        var response = await AskAsync(settings.SafetyModel, photo, GeminiPrompts.SafetyInstruction, GeminiPrompts.SafetySchema(), cancellationToken);

        if (RefusalCategory(response) is { } refusal)
        {
            return new AiAnswer<SafetyVerdict>(SafetyVerdict.Block(refusal), UsageOf(response));
        }

        var answer = Parse(response);
        if (Property(answer, "allowed") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } allowed)
        {
            throw new AiProviderException(AiFailure.InvalidOutput, "The safety check gave no verdict.");
        }

        var verdict = allowed.GetBoolean() ? SafetyVerdict.Allow : SafetyVerdict.Block(CategoryOf(Property(answer, "category")));
        return new AiAnswer<SafetyVerdict>(verdict, UsageOf(response));
    }

    /// <summary>
    /// The objects in the photo. Whatever comes back is untrusted: the boxes are put into
    /// LanguZe's own shape here, and the vocabulary rules decide what may be saved (AIR-003, FR-023).
    /// </summary>
    public async Task<AiAnswer<IReadOnlyList<ExtractedItem>>> ExtractVocabularyAsync(PhotoForAi photo, CancellationToken cancellationToken)
    {
        var response = await AskAsync(settings.ExtractionModel, photo, GeminiPrompts.ExtractionInstruction, GeminiPrompts.ExtractionSchema(), cancellationToken);

        // A refusal here is not a block: the photo already passed the safety check, so this is
        // the provider failing, and the analysis is released (FR-025).
        if (RefusalCategory(response) is not null)
        {
            throw new AiProviderException(AiFailure.ProviderError, "Gemini refused to describe the photo.");
        }

        var answer = Parse(response);
        if (Property(answer, "items") is not { ValueKind: JsonValueKind.Array } items)
        {
            throw new AiProviderException(AiFailure.InvalidOutput, "The extraction returned no list of items.");
        }

        return new AiAnswer<IReadOnlyList<ExtractedItem>>([.. items.EnumerateArray().Select(AsExtractedItem)], UsageOf(response));
    }

    /// <summary>One call, with every way it can fail turned into one of LanguZe's own (FR-025).</summary>
    private async Task<GenerateContentResponse> AskAsync(string model, PhotoForAi photo, string instruction, Schema schema, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        try
        {
            return await models.GenerateContentAsync(
                model,
                [new Content { Role = "user", Parts = [new Part { InlineData = new Blob { MimeType = photo.ContentType, Data = photo.Data.ToArray() } }] }],
                new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = [new Part { Text = instruction }] },
                    ResponseMimeType = "application/json",
                    ResponseSchema = schema,
                    AutomaticContinuation = false,
                },
                deadline.Token);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            throw error is OperationCanceledException or TimeoutException
                ? new AiProviderException(AiFailure.TimedOut, "Gemini did not answer in time", error)
                : new AiProviderException(AiFailure.ProviderError, "Gemini could not answer", error);
        }
    }

    /// <summary>
    /// Structured output still arrives as text, so it still has to be parsed. The text is the
    /// JavaScript SDK's <c>response.text</c>: the first candidate's text parts, thoughts left out.
    /// </summary>
    private static JsonElement Parse(GenerateContentResponse response)
    {
        var parts = response.Candidates?.FirstOrDefault()?.Content?.Parts ?? [];
        var spoken = parts.Where(part => part.Text is not null && part.Thought != true).Select(part => part.Text).ToList();
        var text = string.Concat(spoken);
        if (text.Length == 0)
        {
            throw new AiProviderException(AiFailure.InvalidOutput, "Gemini answered with nothing.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new AiProviderException(AiFailure.InvalidOutput, "Gemini answered with something that is not JSON.", error);
        }
    }

    /// <summary>A property of an answer that should be an object; none for anything else.</summary>
    private static JsonElement? Property(JsonElement answer, string name) =>
        answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty(name, out var value) ? value : null;

    /// <summary>Whether Gemini's own filters stopped the request or the answer, and why.</summary>
    private static BlockCategory? RefusalCategory(GenerateContentResponse response)
    {
        var promptReason = response.PromptFeedback?.BlockReason;
        var finish = response.Candidates?.FirstOrDefault()?.FinishReason;
        if (promptReason?.Value is not { Length: > 0 } && finish?.Value != "SAFETY")
        {
            return null;
        }

        var category = response.PromptFeedback?.SafetyRatings?.FirstOrDefault(rating => rating.Blocked == true)?.Category;
        return category is { } named && RefusalCategories.TryGetValue(named.Value, out var mapped) ? mapped : BlockCategory.Other;
    }

    /// <summary>A category LanguZe knows, or <c>OTHER</c> for anything else the model invents.</summary>
    private static BlockCategory CategoryOf(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } text && Categories.TryGetValue(text.GetString()!, out var category) ? category : BlockCategory.Other;

    private static AiUsage UsageOf(GenerateContentResponse response) =>
        new(response.UsageMetadata?.PromptTokenCount, response.UsageMetadata?.CandidatesTokenCount);

    /// <summary>
    /// One item as LanguZe describes it. Anything missing or misshapen is left as it is and
    /// refused later by the item rules, which is the one place that decides (AIR-003): this only
    /// changes the shape, never the judgement. A field that should be text and is not becomes
    /// empty, never words such as "[object Object]" offered to a learner as vocabulary.
    /// </summary>
    private static ExtractedItem AsExtractedItem(JsonElement raw)
    {
        string Text(string name) => Property(raw, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : string.Empty;

        var box = Property(raw, "box") is { ValueKind: JsonValueKind.Array } numbers ? numbers.EnumerateArray().Select(JsNumber).ToList() : [];
        double At(int index) => index < box.Count ? box[index] : double.NaN;
        var (ymin, xmin, ymax, xmax) = (At(0), At(1), At(2), At(3));

        IReadOnlyList<string> variants = Property(raw, "acceptedVariants") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Where(variant => variant.ValueKind == JsonValueKind.String).Select(variant => variant.GetString()!)]
            : [];

        return new ExtractedItem(
            Text("english"),
            Text("thaiMeaning"),
            Text("exampleSentence"),
            Text("cefrLevel"),
            variants,
            new WordBox(xmin / BoxScale, ymin / BoxScale, (xmax - xmin) / BoxScale, (ymax - ymin) / BoxScale));
    }

    /// <summary>
    /// JavaScript's <c>Number()</c> of a JSON value, which the box goes through: a number as it
    /// is, a numeric string as its number, null and an empty array as 0, and anything else NaN,
    /// which the item rules refuse.
    /// </summary>
    private static double JsNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => JsNumber(value.GetString()!),
        JsonValueKind.True => 1,
        JsonValueKind.False or JsonValueKind.Null => 0,
        JsonValueKind.Array when value.GetArrayLength() == 0 => 0,
        JsonValueKind.Array when value.GetArrayLength() == 1 && value[0].ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Array => JsNumber(value[0]),
        _ => double.NaN,
    };

    private static double JsNumber(string text)
    {
        var trimmed = JsText.Trim(text);
        return trimmed switch
        {
            "" => 0,
            "Infinity" or "+Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ when DecimalLiteral().IsMatch(trimmed) => double.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => double.NaN,
        };
    }

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?\z")]
    private static partial Regex DecimalLiteral();
}
