using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>How an AI call ended (AIR-007); the PostgreSQL enum <c>ai_outcome</c>.</summary>
public enum AiOutcome
{
    [PgName("SUCCEEDED")]
    Succeeded,

    [PgName("FAILED")]
    Failed,

    [PgName("TIMED_OUT")]
    TimedOut,

    [PgName("INVALID_OUTPUT")]
    InvalidOutput,
}
