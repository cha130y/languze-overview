namespace LanguZe.Api.Data.Entities;

/// <summary>
/// What one AI call cost and how it went (data model, <c>ai_calls</c>; AIR-007,
/// NFR-017). No learner, photo, prompt or message is kept: only the job, the model,
/// the time it took and the outcome.
/// </summary>
public sealed class AiCall
{
    public Guid Id { get; set; }

    public AiPurpose Purpose { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public int LatencyMs { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public AiOutcome Outcome { get; set; }

    public string? ErrorCode { get; set; }

    public string? RequestId { get; set; }
}
