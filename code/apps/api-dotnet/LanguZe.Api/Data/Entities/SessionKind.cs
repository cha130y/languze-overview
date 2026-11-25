using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>A practice session of one world, or a review across them (FR-030, FR-050); the PostgreSQL enum <c>session_kind</c>.</summary>
public enum SessionKind
{
    [PgName("GAME")]
    Game,

    [PgName("REVIEW")]
    Review,
}
