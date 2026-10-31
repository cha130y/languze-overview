namespace LanguZe.Api.Modules.Moderation.Contracts;

/// <summary>Why AI features are unavailable.</summary>
public enum AiAccessReason
{
    NotVerified,
    AiSuspended,
}

/// <summary>
/// Whether a learner may use the AI features, and why not (FR-006, FR-096): the decision
/// <c>/v1/me</c> reports and the AI endpoints enforce, so the two never disagree.
/// </summary>
/// <param name="Until">When a suspension ends by itself; empty while it waits for an admin.</param>
public sealed record AiAccess(bool Available, AiAccessReason? Reason, DateTimeOffset? Until, bool AwaitingReview);

/// <summary>Works out a learner's AI access, for other modules (ADR-0006, rule R3).</summary>
public interface IAiAccess
{
    Task<AiAccess> ForAsync(Guid learnerId, bool verifiedForAi, CancellationToken cancellationToken);
}
