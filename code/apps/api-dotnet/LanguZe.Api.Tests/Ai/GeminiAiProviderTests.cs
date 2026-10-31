using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.GenAI.Types;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Ai.Shared.Gemini;
using LanguZe.Api.Modules.Vocabulary.Contracts;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Tests.Ai;

/// <summary>gemini-ai-provider.spec.ts, the photo half: Gemini's answers as the SDK hands them over, and what LanguZe makes of them.</summary>
public sealed partial class GeminiAiProviderTests
{
    private static readonly PhotoForAi Photo = new(Encoding.UTF8.GetBytes("a photo"), "image/jpeg");

    private static readonly int[] TwoNumbers = [1, 2];

    private static readonly GeminiSettings Settings = new("a-key", "safety-model", "extraction-model", "tutor-model", TimeSpan.FromSeconds(30));

    [Fact]
    public async Task The_safety_check_allows_a_photo_gemini_allows()
    {
        var (provider, _) = Gemini(Answering(new { allowed = true }));

        var answer = await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken);

        Assert.Equal(SafetyVerdict.Allow, answer.Value);
        Assert.Equal(new AiUsage(900, 120), answer.Usage);
    }

    [Fact]
    public async Task The_safety_check_blocks_with_the_category_gemini_chose()
    {
        var (provider, _) = Gemini(Answering(new { allowed = false, category = "VIOLENCE" }));

        Assert.Equal(SafetyVerdict.Block(BlockCategory.Violence), (await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task The_safety_check_falls_back_to_OTHER_for_a_category_languze_does_not_know()
    {
        var (provider, _) = Gemini(Answering(new { allowed = false, category = "SOMETHING_ELSE" }));

        Assert.Equal(SafetyVerdict.Block(BlockCategory.Other), (await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken)).Value);
    }

    // ADR-0004: a photo Gemini's own filters refuse is one LanguZe should not keep.
    [Fact]
    public async Task The_safety_check_treats_gemini_refusing_to_look_as_a_block()
    {
        var (provider, _) = Gemini(Response("""{"promptFeedback":{"blockReason":"SAFETY","safetyRatings":[{"category":"HARM_CATEGORY_SEXUALLY_EXPLICIT","blocked":true}]}}"""));

        Assert.Equal(SafetyVerdict.Block(BlockCategory.SexualContent), (await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task The_safety_check_blocks_as_OTHER_when_the_refusal_names_no_category()
    {
        var (provider, _) = Gemini(Response("""{"promptFeedback":{"blockReason":"OTHER"}}"""));

        Assert.Equal(SafetyVerdict.Block(BlockCategory.Other), (await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken)).Value);
    }

    // AIR-006: nothing about the learner travels with a photo; and the JavaScript SDK's defaults hold.
    [Fact]
    public async Task The_safety_check_asks_the_safety_model_with_the_photo_and_fixed_instructions_only()
    {
        var (provider, models) = Gemini(Answering(new { allowed = true }));

        await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken);

        var (model, contents, config) = models.Requests.Single();
        Assert.Equal("safety-model", model);
        Assert.Equal(Photo.Data.ToArray(), contents.Single().Parts!.Single().InlineData!.Data);
        Assert.Equal("image/jpeg", contents.Single().Parts!.Single().InlineData!.MimeType);
        Assert.Equal(GeminiPrompts.SafetyInstruction, config.SystemInstruction!.Parts!.Single().Text);
        Assert.Equal("application/json", config.ResponseMimeType);
        Assert.False(config.AutomaticContinuation);
        Assert.Null(config.HttpOptions);
        var sent = JsonSerializer.Serialize(new { model, contents, config });
        Assert.DoesNotMatch(Identifying(), sent);
    }

    [Fact]
    public async Task The_safety_check_refuses_an_answer_that_gives_no_verdict()
    {
        var (provider, _) = Gemini(Answering(new { category = "VIOLENCE" }));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
    }

    [Fact]
    public async Task Extraction_turns_geminis_boxes_into_languzes()
    {
        var (provider, _) = Gemini(Answering(new { items = new[] { Item() } }));

        var answer = await provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken);

        // [100, 200, 500, 600] of 1000 is x 0.2, y 0.1, width 0.4, height 0.4.
        Assert.Equal(new WordBox(0.2, 0.1, 0.4, 0.4), answer.Value.Single().Box);
    }

    [Fact]
    public async Task Extraction_produces_items_the_vocabulary_rules_accept()
    {
        var (provider, _) = Gemini(Answering(new { items = new[] { Item() } }));

        var answer = await provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken);

        Assert.Single(ExtractionRules.CheckedItems(answer.Value));
    }

    [Fact]
    public async Task Extraction_asks_the_extraction_model_and_reports_what_it_cost()
    {
        var (provider, models) = Gemini(Answering(new { items = new[] { Item() } }));

        var answer = await provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken);

        Assert.Equal("extraction-model", models.Requests.Single().Model);
        Assert.Equal(GeminiPrompts.ExtractionInstruction, models.Requests.Single().Config.SystemInstruction!.Parts!.Single().Text);
        Assert.Equal(new AiUsage(900, 120), answer.Usage);
    }

    // A misshapen item is not judged here: the item rules decide, in one place.
    [Fact]
    public async Task Extraction_passes_a_broken_item_on_rather_than_guessing()
    {
        var (provider, _) = Gemini(Answering(new { items = new object[] { new { thaiMeaning = "โซฟา", exampleSentence = "We sit on the sofa.", cefrLevel = "A1", box = TwoNumbers }, "not an item" } }));

        var items = (await provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(string.Empty, items[0].English);
        Assert.True(double.IsNaN(items[0].Box.Width));
        Assert.Equal(string.Empty, items[1].ThaiMeaning);
        Assert.Empty(ExtractionRules.CheckedItems(items));
    }

    // JavaScript's Number() reads the box: a numeric string is its number.
    [Fact]
    public async Task Extraction_reads_a_box_as_javascripts_number_does()
    {
        var (provider, _) = Gemini(Answering(new { items = new[] { new { english = "sofa", thaiMeaning = "โซฟา", exampleSentence = "We sit on the sofa.", cefrLevel = "A1", acceptedVariants = new object[] { "couch", 7 }, box = new object?[] { "100", " 200 ", null, "a lot" } } } }));

        var item = (await provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken)).Value.Single();

        Assert.Equal(0.2, item.Box.X);
        Assert.Equal(0.1, item.Box.Y);
        Assert.Equal(-0.1, item.Box.Height, 10);
        Assert.True(double.IsNaN(item.Box.Width));
        Assert.Equal(["couch"], item.AcceptedVariants);
    }

    [Fact]
    public async Task Extraction_refuses_an_answer_with_no_list_of_items()
    {
        var (provider, _) = Gemini(Answering(new { words = Array.Empty<string>() }));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
    }

    // The photo already passed the safety check, so a refusal here is the provider.
    [Fact]
    public async Task Extraction_treats_a_refusal_as_a_provider_error_not_a_block()
    {
        var (provider, _) = Gemini(Response("""{"candidates":[{"finishReason":"SAFETY"}]}"""));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.ProviderError, error.Failure);
    }

    [Fact]
    public async Task A_call_past_its_deadline_is_TIMED_OUT()
    {
        var models = new StandInModels(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("never answers");
        });
        var provider = new GeminiAiProvider(models, Settings with { Timeout = TimeSpan.FromMilliseconds(50) });

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.TimedOut, error.Failure);
    }

    [Fact]
    public async Task Anything_else_that_goes_wrong_is_a_provider_error()
    {
        var unavailable = new HttpRequestException("503 Service Unavailable");
        var (provider, _) = Gemini(unavailable);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.ProviderError, error.Failure);
        Assert.Same(unavailable, error.InnerException);
    }

    [Fact]
    public async Task An_empty_answer_is_refused()
    {
        var (provider, _) = Gemini(Response("""{"candidates":[{"content":{"parts":[]}}]}"""));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_refused()
    {
        var (provider, _) = Gemini(Text("I am afraid I cannot do that."));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.ExtractVocabularyAsync(Photo, TestContext.Current.CancellationToken));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
    }

    // As the JavaScript SDK's response.text: a part marked as a thought is the model talking to itself.
    [Fact]
    public async Task A_thought_is_not_part_of_the_answer()
    {
        var (provider, _) = Gemini(Response("""{"candidates":[{"content":{"parts":[{"text":"thinking it over","thought":true},{"text":"{\"allowed\":true}"}]}}]}"""));

        Assert.Equal(SafetyVerdict.Allow, (await provider.CheckPhotoAsync(Photo, TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public void Each_job_uses_its_own_model()
    {
        var (provider, _) = Gemini(Answering(new { }));

        Assert.Equal("gemini", provider.Name);
        Assert.Equal("safety-model", provider.ModelFor(AiPurpose.SafetyCheck));
        Assert.Equal("extraction-model", provider.ModelFor(AiPurpose.Extraction));
        Assert.Equal("tutor-model", provider.ModelFor(AiPurpose.Tutor));
    }

    // The AI evaluation judged prompts.ts's words, so these are its SHA-256s; a change needs an evaluation run (ADR-0004).
    [Fact]
    public void The_instructions_are_prompts_ts_word_for_word()
    {
        Assert.Equal("2026-09-26.1", GeminiPrompts.PromptVersion);
        Assert.Equal("dd9ef1de88b4d4e201ed415140aa23f7601bdc08d62ff3792d44646f7e9de07d", Sha256(GeminiPrompts.SafetyInstruction));
        Assert.Equal("7085c3745e9952f59d9e412f9cc85e65ce6c512d83362c13d4b821d5508d78a7", Sha256(GeminiPrompts.ExtractionInstruction));
    }

    private static (GeminiAiProvider Provider, StandInModels Models) Gemini(GenerateContentResponse response)
    {
        var models = new StandInModels(_ => Task.FromResult(response));
        return (new GeminiAiProvider(models, Settings), models);
    }

    private static (GeminiAiProvider Provider, StandInModels Models) Gemini(Exception error)
    {
        var models = new StandInModels(_ => Task.FromException<GenerateContentResponse>(error));
        return (new GeminiAiProvider(models, Settings), models);
    }

    /// <summary>Gemini answering with <paramref name="body"/> as its JSON, and what the call cost.</summary>
    private static GenerateContentResponse Answering(object body) => Text(JsonSerializer.Serialize(body));

    private static GenerateContentResponse Text(string text) =>
        Response(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { role = "model", parts = new[] { new { text } } } } },
            usageMetadata = new { promptTokenCount = 900, candidatesTokenCount = 120 },
        }));

    private static GenerateContentResponse Response(string json) => GenerateContentResponse.FromJson(json)!;

    /// <summary>[ymin, xmin, ymax, xmax], 0 to 1000 (ADR-0004).</summary>
    private static object Item() => new
    {
        english = "sofa",
        thaiMeaning = "โซฟา",
        exampleSentence = "We sit on the sofa.",
        cefrLevel = "A1",
        acceptedVariants = new[] { "couch" },
        box = new[] { 100, 200, 500, 600 },
    };

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex("email|learner|@", RegexOptions.IgnoreCase)]
    private static partial Regex Identifying();

    /// <summary>A stand-in for the SDK: whatever the test says Gemini answered, and what it was asked.</summary>
    private sealed class StandInModels(Func<CancellationToken, Task<GenerateContentResponse>> answer) : IGeminiModels
    {
        public List<(string Model, List<Content> Contents, GenerateContentConfig Config)> Requests { get; } = [];

        public Task<GenerateContentResponse> GenerateContentAsync(string model, List<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken)
        {
            Requests.Add((model, contents, config));
            return answer(cancellationToken);
        }
    }
}
