using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Where one analysis stands; the counted ones use a day's allowance (FR-020); the PostgreSQL enum <c>analysis_status</c>.</summary>
public enum AnalysisStatus
{
    [PgName("IN_PROGRESS")]
    InProgress,

    [PgName("SUCCEEDED")]
    Succeeded,

    [PgName("BLOCKED")]
    Blocked,

    [PgName("FAILED")]
    Failed,
}
