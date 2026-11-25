using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Where a practice session stands (FR-036, V17); the PostgreSQL enum <c>session_status</c>.</summary>
public enum SessionStatus
{
    [PgName("IN_PROGRESS")]
    InProgress,

    [PgName("COMPLETED")]
    Completed,

    /// <summary>Left unfinished: a day passed, or a new session replaced it (V17, FR-036).</summary>
    [PgName("ABANDONED")]
    Abandoned,
}
