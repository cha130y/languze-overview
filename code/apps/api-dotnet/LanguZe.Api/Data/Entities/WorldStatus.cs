using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Where a world's analysis stands (FR-021); the PostgreSQL enum <c>world_status</c>.</summary>
public enum WorldStatus
{
    [PgName("ANALYZING")]
    Analyzing,

    [PgName("READY")]
    Ready,

    [PgName("FAILED")]
    Failed,
}
