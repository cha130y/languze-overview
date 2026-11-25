using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.GenAI.Types;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Ai.Shared.Gemini;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Tests.Ai;

/// <summary>
/// gemini-ai-provider.spec.ts, the tutor half: Gemini's streamed rounds as the SDK hands them
/// over, the tools it asks for, and what LanguZe makes of it all (FR-070 to FR-075, ADR-0004).
/// </summary>
public sealed partial class GeminiTutorTests
{
    private static readonly GeminiSettings Settings = new("a-key", "safety-model", "extraction-model", "tutor-model", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), 5);

    private static readonly TutorTurn Turn = new([], "ทำไมฉันจำคำนี้ไม่ได้", []);

    private int _weakWordsRuns;

    private readonly List<IReadOnlyDictionary<string, JsonElement>> _weakWordsInputs = [];

    private Func<Task<object>> _weakWords = () => Task.FromResult<object>(new { words = new[] { new { english = "sofa" } } });

    // NFR-003: the learner watches the reply being written.
    [Fact]
    public async Task The_reply_streams_as_it_arrives()
    {
        var (provider, _) = Streaming(Round(Said("คำนี้"), Said("ใช้บอก"), Said("สิ่งของ"), Spent(900, 120)));

        var events = await CollectAsync(provider);

        Assert.Equal(["คำนี้", "ใช้บอก", "สิ่งของ"], events.OfType<TutorDelta>().Select(delta => delta.Text));
        Assert.Equal(new TutorDone(new AiUsage(900, 120)), events[^1]);
    }

    [Fact]
    public async Task The_conversation_goes_oldest_first_with_the_new_message_last()
    {
        var (provider, models) = Streaming(Round(Said("ค่ะ")));

        await CollectAsync(provider, Turn with { History = [new(TutorRole.Learner, "สวัสดี"), new(TutorRole.Tutor, "สวัสดีค่ะ")] });

        Assert.Equal(
            [("user", "สวัสดี"), ("model", "สวัสดีค่ะ"), ("user", "ทำไมฉันจำคำนี้ไม่ได้")],
            models.Requests[0].Contents.Select(content => (content.Role, Assert.Single(content.Parts!).Text)));
    }

    // Gemini signs the function calls it makes, and refuses the next request unless its turn
    // comes back as it was sent (HTTP 400): every tool reply failed until it came back whole.
    [Fact]
    public async Task Geminis_turn_goes_back_as_it_came_thought_signature_and_all()
    {
        var signature = Encoding.UTF8.GetBytes("signature-1");
        var (provider, models) = Streaming(
            Round(Asked("weak_words", new { count = 3 }, signature), Spent(400, 10)),
            Round(Said("คำที่ยากคือ sofa"), Spent(600, 40)));

        await CollectAsync(provider);

        var sentBack = models.Requests[1].Contents[1];
        Assert.Equal("model", sentBack.Role);
        var part = Assert.Single(sentBack.Parts!);
        Assert.Equal(("weak_words", signature), (part.FunctionCall!.Name, part.ThoughtSignature));
    }

    [Fact]
    public async Task A_part_marked_as_a_thought_is_never_shown()
    {
        var (provider, _) = Streaming(Round(Chunk(new Part { Text = "Let me think about sofa.", Thought = true }), Said("คำนี้คือ sofa"), Spent(400, 10)));

        Assert.Equal("คำนี้คือ sofa", TextOf(await CollectAsync(provider)));
    }

    [Fact]
    public async Task The_tool_runs_its_result_goes_back_and_gemini_answers()
    {
        var (provider, models) = Streaming(
            Round(Asked("weak_words", new { count = 3 }), Spent(400, 10)),
            Round(Said("คำที่ยากคือ sofa"), Spent(600, 40)));

        var events = await CollectAsync(provider);

        Assert.Equal(3, Assert.Single(_weakWordsInputs)["count"].GetInt32());
        Assert.Equal("คำที่ยากคือ sofa", TextOf(events));
        var contents = models.Requests[1].Contents;
        Assert.Equal(3, contents.Count);
        Assert.Equal("user", contents[2].Role);
        var answer = Assert.Single(contents[2].Parts!).FunctionResponse!;
        Assert.Equal("weak_words", answer.Name);
        Assert.Equal("""{"words":[{"english":"sofa"}]}""", JsonSerializer.Serialize(answer.Response));
    }

    // A call Gemini gave an ID is answered under that ID.
    [Fact]
    public async Task A_call_with_an_id_is_answered_under_it()
    {
        var (provider, models) = Streaming(Round(Chunk(new Part { FunctionCall = new FunctionCall { Id = "call-7", Name = "weak_words", Args = new() } })), Round(Said("ค่ะ")));

        await CollectAsync(provider);

        Assert.Equal("call-7", Assert.Single(models.Requests[1].Contents[2].Parts!).FunctionResponse!.Id);
    }

    // A tool result reaches Gemini as the API writes JSON, an instant as toISOString writes it.
    [Fact]
    public async Task A_tool_result_is_written_as_the_api_writes_json()
    {
        _weakWords = () => Task.FromResult<object>(new Practised("sofa", DateTimeOffset.Parse("2026-10-02T18:00:00.5+07:00", System.Globalization.CultureInfo.InvariantCulture)));
        var (provider, models) = Streaming(Round(Asked("weak_words")), Round(Said("ค่ะ")));

        await CollectAsync(provider);

        Assert.Equal(
            """{"english":"sofa","lastPractisedAt":"2026-10-02T11:00:00.500Z"}""",
            JsonSerializer.Serialize(Assert.Single(models.Requests[1].Contents[2].Parts!).FunctionResponse!.Response));
    }

    // Every round is billed, so the learner's message costs all of them.
    [Fact]
    public async Task What_every_round_cost_is_added_up()
    {
        var (provider, _) = Streaming(Round(Asked("weak_words"), Spent(400, 10)), Round(Said("พร้อมแล้วค่ะ"), Spent(600, 40)));

        Assert.Equal(new TutorDone(new AiUsage(1000, 50)), (await CollectAsync(provider))[^1]);
    }

    // FR-072 at the protocol boundary: the parameters Gemini fills in never name a learner.
    [Fact]
    public async Task Each_tool_is_described_to_gemini_without_a_learner_anywhere_gemini_writes()
    {
        var (provider, models) = Streaming(Round(Said("ค่ะ")));

        await CollectAsync(provider);

        var declared = Assert.Single(Assert.Single(models.Requests[0].Config.Tools!).FunctionDeclarations!);
        Assert.Equal(("weak_words", "The learner's weakest words."), (declared.Name, declared.Description));
        Assert.Equal(Google.GenAI.Types.Type.Object, declared.Parameters!.Type);
        var (name, schema) = Assert.Single(declared.Parameters.Properties!);
        Assert.Equal(("count", Google.GenAI.Types.Type.Integer, "How many."), (name, schema.Type, schema.Description));
        Assert.DoesNotMatch(LearnerWords(), name);
        Assert.Null(declared.Parameters.Required);
    }

    // A model inventing a tool is not a reason to fail the learner's message.
    [Fact]
    public async Task Gemini_is_told_when_it_asks_for_a_tool_that_does_not_exist()
    {
        var (provider, models) = Streaming(Round(Asked("other_learners")), Round(Said("ขออภัยค่ะ")));

        var events = await CollectAsync(provider);

        var answer = Assert.Single(models.Requests[1].Contents[2].Parts!).FunctionResponse!;
        Assert.Equal("other_learners", answer.Name);
        Assert.Equal("""{"error":"There is no tool called other_learners."}""", JsonSerializer.Serialize(answer.Response));
        Assert.Equal("ขออภัยค่ะ", TextOf(events));
    }

    // ADR-0004 gives the whole reply its time, not each round: the same token reaches every round.
    [Fact]
    public async Task The_whole_turn_has_one_deadline_not_one_for_each_round()
    {
        var (provider, models) = Streaming(Round(Asked("weak_words")), Round(Said("ค่ะ")));

        await CollectAsync(provider);

        Assert.True(models.Requests[0].Token.CanBeCanceled);
        Assert.Equal(models.Requests[0].Token, models.Requests[1].Token);
    }

    [Fact]
    public async Task A_turn_past_its_deadline_is_TIMED_OUT_even_across_rounds()
    {
        var (provider, _) = Streaming(Settings with { TutorTimeout = TimeSpan.FromMilliseconds(200) }, new Slow(TimeSpan.FromMilliseconds(150), Round(Asked("weak_words"))), new Slow(TimeSpan.FromMilliseconds(150), Round(Asked("weak_words"))), Round(Said("ค่ะ")));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => CollectAsync(provider));

        Assert.Equal(AiFailure.TimedOut, error.Failure);
    }

    // ADR-0004 bounds one message to five tool rounds; past that it costs without end.
    [Fact]
    public async Task A_model_that_only_ever_looks_things_up_is_stopped()
    {
        var (provider, models) = Streaming(Round(Asked("weak_words")));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => CollectAsync(provider));

        Assert.Equal(AiFailure.ProviderError, error.Failure);
        Assert.Equal("Gemini was still looking things up after 5 rounds.", error.Message);
        Assert.Equal(Settings.MaxToolRounds + 1, models.Requests.Count);
        Assert.Equal(Settings.MaxToolRounds, _weakWordsRuns);
    }

    // AIR-006: the turn holds nothing but the conversation, so nothing else is sent.
    [Fact]
    public async Task Nothing_that_identifies_the_learner_is_sent()
    {
        var (provider, models) = Streaming(Round(Said("ค่ะ")));

        await CollectAsync(provider);

        var (model, _, config, _) = models.Requests[0];
        Assert.Equal("tutor-model", model);
        Assert.Equal(GeminiPrompts.TutorInstruction, Assert.Single(config.SystemInstruction!.Parts!).Text);
        Assert.Equal((false, null, null, null), (config.AutomaticContinuation, config.ResponseMimeType, config.ResponseSchema, config.Labels));
    }

    [Fact]
    public async Task A_timeout_is_TIMED_OUT()
    {
        var (provider, _) = Streaming(Settings with { TutorTimeout = TimeSpan.FromMilliseconds(50) }, Waiting(Timeout.InfiniteTimeSpan));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => CollectAsync(provider));

        Assert.Equal(AiFailure.TimedOut, error.Failure);
    }

    [Fact]
    public async Task Anything_else_is_a_provider_error()
    {
        var unavailable = new HttpRequestException("503 Service Unavailable");
        var (provider, _) = Streaming(unavailable);

        var error = await Assert.ThrowsAsync<AiProviderException>(() => CollectAsync(provider));

        Assert.Equal(AiFailure.ProviderError, error.Failure);
        Assert.Same(unavailable, error.InnerException);
    }

    // Gemini's own filters refusing leave no reply, and an empty one must not be saved.
    [Fact]
    public async Task An_answer_with_nothing_in_it_is_refused()
    {
        var (provider, _) = Streaming(Round(Said(string.Empty)));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => CollectAsync(provider));

        Assert.Equal((AiFailure.InvalidOutput, "Gemini answered with nothing."), (error.Failure, error.Message));
    }

    // A tool that fails fails the turn as it is, not as the provider's fault.
    [Fact]
    public async Task A_tool_that_fails_fails_the_turn()
    {
        _weakWords = () => throw new InvalidOperationException("the database is down");
        var (provider, _) = Streaming(Round(Asked("weak_words")), Round(Said("ค่ะ")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(provider));

        Assert.Equal("the database is down", error.Message);
    }

    private static (GeminiAiProvider Provider, StreamingModels Models) Streaming(params object[] rounds) => Streaming(Settings, rounds);

    private static (GeminiAiProvider Provider, StreamingModels Models) Streaming(GeminiSettings settings, params object[] rounds)
    {
        var models = new StreamingModels(rounds);
        return (new GeminiAiProvider(models, settings), models);
    }

    private async Task<List<TutorEvent>> CollectAsync(GeminiAiProvider provider, TutorTurn? turn = null)
    {
        var tool = new TutorTool("weak_words", "The learner's weakest words.", [new("count", "integer", "How many.")], (input, _) =>
        {
            _weakWordsRuns++;
            _weakWordsInputs.Add(input);
            return _weakWords();
        });
        var events = new List<TutorEvent>();
        await foreach (var tutorEvent in provider.TutorReplyAsync((turn ?? Turn) with { Tools = [tool] }, TestContext.Current.CancellationToken))
        {
            events.Add(tutorEvent);
        }

        return events;
    }

    /// <summary>One streamed round: the chunks Gemini sends back for a single call.</summary>
    private static Chunks Round(params GenerateContentResponse[] chunks) => new(chunks);

    private static string TextOf(List<TutorEvent> events) => string.Concat(events.OfType<TutorDelta>().Select(delta => delta.Text));

    /// <summary>A chunk as the SDK delivers it: Gemini's parts, under its one candidate.</summary>
    private static GenerateContentResponse Chunk(params Part[] parts) =>
        new() { Candidates = [new Candidate { Content = new Content { Role = "model", Parts = [.. parts] } }] };

    private static GenerateContentResponse Said(string text) => Chunk(new Part { Text = text });

    private static GenerateContentResponse Asked(string name, object? args = null, byte[]? thoughtSignature = null) =>
        Chunk(new Part
        {
            FunctionCall = new FunctionCall { Name = name, Args = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(args ?? new { })) },
            ThoughtSignature = thoughtSignature,
        });

    private static GenerateContentResponse Spent(int inputTokens, int outputTokens) =>
        GenerateContentResponse.FromJson($$$"""{"usageMetadata":{"promptTokenCount":{{{inputTokens}}},"candidatesTokenCount":{{{outputTokens}}}}}""")!;

    /// <summary>A round that sends nothing until <paramref name="wait"/> has passed or the call is cancelled.</summary>
    private static Func<CancellationToken, Task> Waiting(TimeSpan wait) => token => Task.Delay(wait, token);

    [GeneratedRegex("learner|user|account", RegexOptions.IgnoreCase)]
    private static partial Regex LearnerWords();

    private sealed record Chunks(GenerateContentResponse[] Sent);

    /// <summary>A round that sends its chunks only once <paramref name="Wait"/> has passed, each round within any one deadline per round.</summary>
    private sealed record Slow(TimeSpan Wait, Chunks Then);

    private sealed record Practised(string English, DateTimeOffset LastPractisedAt);

    /// <summary>
    /// A stand-in for the SDK that streams the rounds the test wrote out, the last one again
    /// for any call past them: chunks, a wait, or a failure. Each request keeps a copy of the
    /// conversation as it was sent.
    /// </summary>
    private sealed class StreamingModels(object[] rounds) : IGeminiModels
    {
        public List<(string Model, List<Content> Contents, GenerateContentConfig Config, CancellationToken Token)> Requests { get; } = [];

        public Task<GenerateContentResponse> GenerateContentAsync(string model, List<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The tutor always streams.");

        public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(string model, List<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken)
        {
            Requests.Add((model, [.. contents], config, cancellationToken));
            return ChunksAsync(rounds[Math.Min(Requests.Count - 1, rounds.Length - 1)], cancellationToken);
        }

        private static async IAsyncEnumerable<GenerateContentResponse> ChunksAsync(object round, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            switch (round)
            {
                case Exception error:
                    throw error;
                case Func<CancellationToken, Task> wait:
                    await wait(cancellationToken);
                    break;
                case Slow slow:
                    await Task.Delay(slow.Wait, cancellationToken);
                    foreach (var chunk in slow.Then.Sent)
                    {
                        yield return chunk;
                    }

                    break;
                case Chunks chunks:
                    foreach (var chunk in chunks.Sent)
                    {
                        await Task.Yield();
                        yield return chunk;
                    }

                    break;
            }
        }
    }
}
