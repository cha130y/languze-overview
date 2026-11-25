namespace LanguZe.Api.Data.Entities;

/// <summary>One sitting of practice, for a world (<c>GAME</c>) or across worlds (<c>REVIEW</c>) (data model, <c>practice_sessions</c>).</summary>
public sealed class PracticeSession
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public SessionKind Kind { get; set; }

    /// <summary>Set for a game only, and emptied when the world goes; the session stays (FR-036).</summary>
    public Guid? WorldId { get; set; }

    public SessionStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<SessionQuestion> Questions { get; set; } = [];
}
