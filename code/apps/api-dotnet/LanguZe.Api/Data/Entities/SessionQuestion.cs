namespace LanguZe.Api.Data.Entities;

/// <summary>One question of a session, fixed when it starts, in the order asked (data model, <c>session_questions</c>).</summary>
public sealed class SessionQuestion
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public PracticeSession Session { get; set; } = null!;

    /// <summary>1 to 10, the order the learner sees.</summary>
    public short Position { get; set; }

    /// <summary>Emptied when the word goes; the question is then skipped (UC-030 3a).</summary>
    public Guid? VocabularyWordId { get; set; }

    /// <summary>Emptied when the word leaves the world.</summary>
    public Guid? OccurrenceId { get; set; }

    public Attempt? Attempt { get; set; }
}
