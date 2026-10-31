namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A pause on a learner's AI features (data model, <c>ai_suspensions</c>; FR-096, FR-103,
/// FR-105). Whether one is in force is worked out from its times, so one whose end has
/// passed stops counting with nothing to run.
/// </summary>
public sealed class AiSuspension
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public SuspensionReason Reason { get; set; }

    /// <summary>Queue order, oldest first (FR-103).</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Empty means "until an admin reviews"; set or moved by an admin (FR-105).</summary>
    public DateTimeOffset? EndsAt { get; set; }

    public DateTimeOffset? LiftedAt { get; set; }

    public Guid? LiftedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
