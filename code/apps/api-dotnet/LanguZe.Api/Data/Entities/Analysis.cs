namespace LanguZe.Api.Data.Entities;

/// <summary>
/// One run of photo analysis, which is what the daily limit counts (data model,
/// <c>analyses</c>; FR-020, FR-025). It outlives its world, so a deleted world still
/// counts toward today.
/// </summary>
public sealed class Analysis
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid? WorldId { get; set; }

    public AnalysisStatus Status { get; set; }

    public AnalysisFailure? FailureReason { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
