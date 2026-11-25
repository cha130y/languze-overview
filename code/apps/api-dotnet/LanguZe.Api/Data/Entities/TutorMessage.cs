namespace LanguZe.Api.Data.Entities;

/// <summary>One thing said in a learner's one conversation with the tutor (data model, <c>tutor_messages</c>; FR-070, V6).</summary>
public sealed class TutorMessage
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public TutorRole Role { get; set; }

    /// <summary>A learner's message is 1 to 1,000 characters (V7); a reply is not capped.</summary>
    public string Content { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
