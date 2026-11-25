namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A learner's total XP (data model, <c>learner_xp</c>), kept apart from the attempts that
/// earned it, so deleting a world or a word never takes points away (FR-060, V4).
/// </summary>
public sealed class LearnerXp
{
    public Guid LearnerId { get; set; }

    public int TotalXp { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
