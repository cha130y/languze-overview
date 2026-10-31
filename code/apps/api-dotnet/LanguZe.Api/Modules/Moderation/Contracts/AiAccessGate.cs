using LanguZe.Api.Platform.Errors;

namespace LanguZe.Api.Modules.Moderation.Contracts;

/// <summary>The refusal an AI endpoint gives when the AI features are closed (suspension.service.ts, <c>requireAccess</c>; FR-006, FR-096).</summary>
public static class AiAccessGate
{
    /// <summary>
    /// Refuses unless the learner may use the AI features: NOT_VERIFIED with the endpoint's
    /// own words, or AI_SUSPENDED with when the pause ends and whether an admin must lift it.
    /// </summary>
    public static async Task RequireAsync(this IAiAccess access, Guid learnerId, bool verifiedForAi, string whenNotVerified, CancellationToken cancellationToken)
    {
        var decision = await access.ForAsync(learnerId, verifiedForAi, cancellationToken);
        if (decision.Available)
        {
            return;
        }

        if (decision.Reason == AiAccessReason.NotVerified)
        {
            throw new AppException(ErrorCodes.NotVerified, StatusCodes.Status403Forbidden, whenNotVerified);
        }

        throw new AppException(
            ErrorCodes.AiSuspended,
            StatusCodes.Status403Forbidden,
            "AI features are paused for this account.",
            new Dictionary<string, object?> { ["until"] = decision.Until, ["awaitingReview"] = decision.AwaitingReview });
    }
}
