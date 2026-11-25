namespace LanguZe.Api.Data.Entities;

/// <summary>
/// One admin action, as the audit log keeps it (data model, <c>admin_actions</c>; FR-106). The
/// table is append-only: triggers refuse every update, delete and truncate (D4). No foreign key
/// holds the learner, so the entry outlives their account, and the ID is all it keeps of them.
/// </summary>
public sealed class AdminAction
{
    public Guid Id { get; set; }

    public Guid AdminId { get; set; }

    public Guid LearnerId { get; set; }

    public AdminActionType Action { get; set; }

    /// <summary>1 to 500 characters that say something (FR-105, V19).</summary>
    public string Reason { get; set; } = "";

    /// <summary>What the action carried beyond its kind, as JSON: the end date set and the one it replaced.</summary>
    public string? Details { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
