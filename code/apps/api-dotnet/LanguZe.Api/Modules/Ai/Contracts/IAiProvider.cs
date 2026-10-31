using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Vocabulary.Contracts;

namespace LanguZe.Api.Modules.Ai.Contracts;

/// <summary>A prepared photo on its way to a provider: at most 2,048 pixels (FR-017, V18).</summary>
public sealed record PhotoForAi(ReadOnlyMemory<byte> Data, string ContentType);

/// <summary>What a provider reports about one call, when it reports anything.</summary>
public sealed record AiUsage(int? InputTokens, int? OutputTokens);

/// <summary>
/// An answer and what it cost. The usage travels with the answer so that the caller can
/// record it without asking the provider afterwards (AIR-007).
/// </summary>
public sealed record AiAnswer<T>(T Value, AiUsage? Usage);

/// <summary>
/// What the safety check decided (FR-091, FR-092). A refusal by the provider's own safety
/// filters counts as a block, with <c>OTHER</c> when no category fits (ADR-0004).
/// </summary>
public sealed record SafetyVerdict(bool Allowed, BlockCategory? Category)
{
    public static readonly SafetyVerdict Allow = new(true, null);

    public static SafetyVerdict Block(BlockCategory category) => new(false, category);
}

/// <summary>Why a provider call gave no answer, in ai-provider.ts's words.</summary>
public enum AiFailure
{
    ProviderError,
    TimedOut,
    InvalidOutput,
}

/// <summary>
/// Why a provider call could not produce an answer. This is never a block: a failure is
/// LanguZe's or the provider's, so the analysis fails and is not counted (FR-025).
/// </summary>
public sealed class AiProviderException(AiFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public AiFailure Failure { get; } = failure;

    /// <summary>The code ai-provider.ts gives it, which the call records keep.</summary>
    public string Code => Failure switch
    {
        AiFailure.TimedOut => "TIMED_OUT",
        AiFailure.InvalidOutput => "INVALID_OUTPUT",
        _ => "PROVIDER_ERROR",
    };
}

/// <summary>
/// What LanguZe asks an AI provider to do (ai-provider.ts; AIR-001). Business rules depend
/// on this interface and never on a provider's SDK, so a provider change is a new adapter.
/// The two photo jobs are separate calls on purpose: a photo is classified before it is
/// described, so a blocked photo never reaches extraction (FR-092, ADR-0004). The tutor's
/// part arrives with the tutor slice.
/// </summary>
public interface IAiProvider
{
    /// <summary>The provider's name, as the call records show it (AIR-007).</summary>
    string Name { get; }

    /// <summary>Which model answers one kind of question; they differ per job (ADR-0004).</summary>
    string ModelFor(AiPurpose purpose);

    /// <summary>Whether this photo may be used at all (FR-091, FR-092).</summary>
    Task<AiAnswer<SafetyVerdict>> CheckPhotoAsync(PhotoForAi photo, CancellationToken cancellationToken);

    /// <summary>
    /// The objects in the photo, in the provider's own order of confidence. Whatever it
    /// returns is untrusted input: the caller checks every item (AIR-003, FR-023).
    /// </summary>
    Task<AiAnswer<IReadOnlyList<ExtractedItem>>> ExtractVocabularyAsync(PhotoForAi photo, CancellationToken cancellationToken);
}
