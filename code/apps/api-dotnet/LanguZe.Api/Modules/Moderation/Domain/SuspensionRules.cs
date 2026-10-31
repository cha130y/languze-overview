using LanguZe.Api.Modules.Moderation.Contracts;

namespace LanguZe.Api.Modules.Moderation.Domain;

/// <summary>Why a pause is raised; the database's <c>suspension_reason</c>, kept here so the rules need no database.</summary>
public enum PauseReason
{
    /// <summary>Level 1: the threshold with no earlier suspension (V15).</summary>
    RepeatedBlocks,

    /// <summary>Level 2: the threshold again, after an earlier suspension (V15).</summary>
    RepeatedAfterSuspension,

    /// <summary>One photo of suspected illegal material (FR-098).</summary>
    SuspectedIllegalMaterial,
}

/// <summary>What a learner's record says at the moment a photo is blocked.</summary>
/// <param name="SuspectedIllegalMaterial">Whether this photo was blocked as suspected illegal material.</param>
/// <param name="BlocksInWindow">Blocked photos in the last seven days, this one included.</param>
/// <param name="SuspendedBefore">Whether this learner has ever been suspended, lifted or not.</param>
public sealed record BlockOutcome(bool SuspectedIllegalMaterial, int BlocksInWindow, bool SuspendedBefore);

/// <summary>A suspension to raise: its reason, and when it lifts itself; never, while it waits for an admin (FR-103).</summary>
public sealed record SuspensionToRaise(PauseReason Reason, DateTimeOffset? EndsAt);

/// <summary>As much of a suspension as deciding a learner's status needs.</summary>
public sealed record SuspensionRecord(DateTimeOffset StartsAt, DateTimeOffset? EndsAt, DateTimeOffset? LiftedAt);

/// <summary>
/// Which pause a learner is under, and what it means for the AI features (suspension-rules.ts
/// and ai-access.ts; data model, <c>ai_suspensions</c>). Worked out from the records rather
/// than stored, so a suspension whose end has passed needs no job to end it.
/// </summary>
public static class SuspensionRules
{
    /// <summary>How many blocked photos reach the threshold, and over what span (V15).</summary>
    public const int BlocksBeforeSuspension = 3;

    public static readonly TimeSpan BlockWindow = TimeSpan.FromDays(7);

    /// <summary>How long a first suspension lasts before it lifts itself (V15, level 1).</summary>
    public static readonly TimeSpan LevelOne = TimeSpan.FromDays(7);

    /// <summary>The start of the window "3 blocked photos within 7 days" counts over.</summary>
    public static DateTimeOffset BlockWindowFrom(DateTimeOffset now) => now - BlockWindow;

    /// <summary>
    /// The suspension a blocked photo earns, or none (suspension-rules.ts, <c>suspensionFor</c>).
    /// Suspected illegal material suspends at once whatever the count, because the count is
    /// about a pattern and this is about one photo (FR-098). Everything else waits for the
    /// threshold, and whether the learner has been here before decides whether the pause ends
    /// by itself or waits for a person (V15).
    /// </summary>
    public static SuspensionToRaise? For(BlockOutcome outcome, DateTimeOffset now)
    {
        if (outcome.SuspectedIllegalMaterial)
        {
            return new SuspensionToRaise(PauseReason.SuspectedIllegalMaterial, null);
        }

        if (outcome.BlocksInWindow < BlocksBeforeSuspension)
        {
            return null;
        }

        return outcome.SuspendedBefore
            ? new SuspensionToRaise(PauseReason.RepeatedAfterSuspension, null)
            : new SuspensionToRaise(PauseReason.RepeatedBlocks, now + LevelOne);
    }

    /// <summary>The suspension in force that started last, or none.</summary>
    public static SuspensionRecord? Current(IEnumerable<SuspensionRecord> all, DateTimeOffset now)
    {
        SuspensionRecord? current = null;
        foreach (var one in all)
        {
            if (IsInForce(one, now) && (current is null || one.StartsAt > current.StartsAt))
            {
                current = one;
            }
        }

        return current;
    }

    /// <summary>Not lifted, and either waiting for review or not yet at its end.</summary>
    public static bool IsInForce(SuspensionRecord suspension, DateTimeOffset now) =>
        suspension.LiftedAt is null && (suspension.EndsAt is null || suspension.EndsAt > now);

    /// <summary>Whether a suspension waits for a person rather than for a date (FR-103).</summary>
    public static bool AwaitsReview(SuspensionRecord suspension) => suspension.EndsAt is null;

    /// <summary>
    /// A suspension is reported ahead of an unverified address, because it is the one a
    /// learner cannot act on: told to verify their email, someone suspended would do it
    /// and still be refused.
    /// </summary>
    public static AiAccess Access(bool verifiedForAi, SuspensionRecord? suspension)
    {
        if (suspension is not null)
        {
            return new AiAccess(false, AiAccessReason.AiSuspended, suspension.EndsAt, AwaitsReview(suspension));
        }

        return verifiedForAi
            ? new AiAccess(true, null, null, false)
            : new AiAccess(false, AiAccessReason.NotVerified, null, false);
    }
}
