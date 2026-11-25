using System.Runtime.CompilerServices;
using System.Text.Json;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Vocabulary.Contracts;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Modules.Ai.Shared;

/// <summary>
/// The AI provider used in development and in tests (fake-ai-provider.ts; B4). It answers
/// at once, costs nothing, and can be told to take any path the real one might: a blocked
/// photo, a provider error, a timeout, too few words, or output that fails every rule.
/// Fixed answers make the rest of the pipeline testable: the same photo always gives the
/// same words, so a test can assert what a learner ends up with.
/// </summary>
internal sealed class FakeAiProvider(FakeAiSettings settings, TimeProvider clock) : IAiProvider
{
    /// <summary>
    /// A photo of a living room, as a provider would describe it: ordered by confidence,
    /// with boxes that do not overlap much and sentences an A1–B1 learner can read.
    /// </summary>
    public static readonly IReadOnlyList<ExtractedItem> LivingRoom =
    [
        new("sofa", "โซฟา", "We sit on the sofa every evening.", "A1", ["couch"], new(0.05, 0.45, 0.4, 0.35)),
        new("curtain", "ผ้าม่าน", "Please open the curtain in the morning.", "A2", ["curtains", "drape"], new(0.55, 0.05, 0.3, 0.6)),
        new("coffee table", "โต๊ะกลาง", "My book is on the coffee table.", "A2", ["table"], new(0.2, 0.7, 0.25, 0.18)),
        new("rug", "พรม", "The rug under the table is soft.", "B1", ["carpet"], new(0.12, 0.78, 0.5, 0.18)),
        new("plant", "ต้นไม้ในกระถาง", "I water the plant on Sunday.", "A1", ["houseplant"], new(0.72, 0.55, 0.18, 0.3)),
        new("clock", "นาฬิกาแขวน", "The clock on the wall is slow.", "A1", [], new(0.78, 0.08, 0.12, 0.12)),
    ];

    /// <summary>Two items, so the analysis fails the "at least three words" rule (FR-024).</summary>
    private static readonly IReadOnlyList<ExtractedItem> AlmostNothing = [.. LivingRoom.Take(2)];

    /// <summary>
    /// Output that looks like a provider having a bad day: an empty word, a meaning with no
    /// Thai in it, a box outside the photo, and a sentence that never uses the word. Every
    /// item is refused, so the analysis fails as invalid output (FR-023, AIR-003).
    /// </summary>
    private static readonly IReadOnlyList<ExtractedItem> Nonsense =
    [
        new(string.Empty, "โซฟา", "We sit here.", "A1", [], new(0.1, 0.1, 0.2, 0.2)),
        new("lamp", "lamp", "The lamp is bright.", "A1", [], new(0.1, 0.1, 0.2, 0.2)),
        new("window", "หน้าต่าง", "The room is bright.", "Z9", [], new(0.9, 0.9, 0.5, 0.5)),
    ];

    /// <summary>The fake tutor's reply, piece by piece.</summary>
    public static readonly IReadOnlyList<string> TutorPieces = ["เข้าใจแล้วค่ะ ", "คำนี้ใช้บอกสิ่งของในห้อง ", "ตัวอย่างเช่น \"The sofa is soft.\""];

    private readonly Lock _gate = new();

    /// <summary>Used once, then forgotten: for a test that needs one call to differ.</summary>
    private string? _next;

    public string Name => "fake";

    /// <summary>What every call does unless a test asks for something else.</summary>
    public string Behaviour { get; set; } = settings.Behaviour;

    /// <summary>Which rule a blocked photo breaks (FR-091).</summary>
    public BlockCategory BlockCategory { get; set; } = BlockCategory.Violence;

    /// <summary>
    /// For a test: while set, every safety check waits here until it completes, so a test can see
    /// how many analyses run at once (<see cref="Holding"/>).
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Safety checks waiting on <see cref="Hold"/> right now.</summary>
    public int Holding => Volatile.Read(ref _holding);

    private int _holding;

    /// <summary>What this provider was asked, in order, with the size of each photo.</summary>
    public List<(AiPurpose Purpose, int Bytes)> Calls { get; } = [];

    public string ModelFor(AiPurpose purpose) => $"fake-{PurposeName(purpose)}";

    public void BehaveOnce(string behaviour)
    {
        lock (_gate)
        {
            _next = behaviour;
        }
    }

    public async Task<AiAnswer<SafetyVerdict>> CheckPhotoAsync(PhotoForAi photo, CancellationToken cancellationToken)
    {
        if (Hold is { } hold)
        {
            Interlocked.Increment(ref _holding);
            try
            {
                await hold.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _holding);
            }
        }

        var behaviour = Take(AiPurpose.SafetyCheck, photo.Data.Length);
        await WaitAsync(cancellationToken);
        ThrowFor(behaviour);
        var verdict = behaviour == "BLOCKED" ? SafetyVerdict.Block(BlockCategory) : SafetyVerdict.Allow;
        // A made-up but plausible cost, so the numbers recorded are not empty.
        return new AiAnswer<SafetyVerdict>(verdict, new AiUsage(780, 12));
    }

    public async Task<AiAnswer<IReadOnlyList<ExtractedItem>>> ExtractVocabularyAsync(PhotoForAi photo, CancellationToken cancellationToken)
    {
        var behaviour = Take(AiPurpose.Extraction, photo.Data.Length);
        await WaitAsync(cancellationToken);
        ThrowFor(behaviour);
        var value = behaviour switch
        {
            "TOO_FEW_WORDS" => AlmostNothing,
            "INVALID_OUTPUT" => Nonsense,
            _ => LivingRoom,
        };
        return new AiAnswer<IReadOnlyList<ExtractedItem>>(value, new AiUsage(820, 40 * value.Count));
    }

    /// <summary>
    /// A tutor reply, in pieces, as a real one arrives (NFR-003). It looks up the learner's weak
    /// words whenever it is asked anything, which is how the tool path gets exercised without a
    /// key: everything a real tutor does to the database, this does too. What it says is fixed,
    /// because a fake that invented answers would teach a test nothing.
    /// </summary>
    public async IAsyncEnumerable<TutorEvent> TutorReplyAsync(TutorTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var behaviour = Take(AiPurpose.Tutor, turn.Message.Length);
        LastHistory = turn.History;
        ThrowFor(behaviour);

        var weak = turn.Tools.FirstOrDefault(tool => tool.Name == "weak_words");
        // The tool result is data about the learner; it is never acted upon here.
        LastToolResult = weak is null ? null : await weak.Run(new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(3) }, cancellationToken);

        foreach (var piece in TutorPieces)
        {
            await WaitAsync(cancellationToken);
            yield return new TutorDelta(piece);
        }

        yield return new TutorDone(new AiUsage(400, 60));
    }

    /// <summary>What the last reply looked up, which a test reads to check the tool path.</summary>
    public object? LastToolResult { get; private set; }

    /// <summary>What the last reply was reminded of, which a test reads to check the history.</summary>
    public IReadOnlyList<TutorExchange> LastHistory { get; private set; } = [];

    /// <summary>The purpose as ai_purpose spells it, lower case.</summary>
    private static string PurposeName(AiPurpose purpose) => purpose switch
    {
        AiPurpose.SafetyCheck => "safety_check",
        AiPurpose.Extraction => "extraction",
        _ => "tutor",
    };

    private static void ThrowFor(string behaviour)
    {
        switch (behaviour)
        {
            case "PROVIDER_ERROR":
                throw new AiProviderException(AiFailure.ProviderError, "The fake provider refused.");
            case "TIMED_OUT":
                throw new AiProviderException(AiFailure.TimedOut, "The fake provider timed out.");
        }
    }

    /// <summary>The behaviour a call uses: a one-off is consumed here, so the next call falls back to the standing one.</summary>
    private string Take(AiPurpose purpose, int bytes)
    {
        lock (_gate)
        {
            Calls.Add((purpose, bytes));
            var behaviour = _next ?? Behaviour;
            _next = null;
            return behaviour;
        }
    }

    /// <summary>Answers after <c>AI_FAKE_DELAY_MS</c>, so a developer can watch the page wait (FR-021).</summary>
    private Task WaitAsync(CancellationToken cancellationToken) =>
        settings.Delay > TimeSpan.Zero ? Task.Delay(settings.Delay, clock, cancellationToken) : Task.CompletedTask;
}
