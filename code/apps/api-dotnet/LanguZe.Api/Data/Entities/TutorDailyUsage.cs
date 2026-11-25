namespace LanguZe.Api.Data.Entities;

/// <summary>
/// How many tutor messages a learner has used on one Bangkok day (data model,
/// <c>tutor_daily_usage</c>; FR-071, V1). Clearing the conversation leaves it alone (US-073).
/// </summary>
public sealed class TutorDailyUsage
{
    public Guid LearnerId { get; set; }

    public DateOnly Day { get; set; }

    public short MessagesUsed { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
