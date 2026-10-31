using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>A word's CEFR level (AIR-003); the PostgreSQL enum <c>cefr_level</c>.</summary>
public enum CefrLevel
{
    [PgName("A1")]
    A1,

    [PgName("A2")]
    A2,

    [PgName("B1")]
    B1,

    [PgName("B2")]
    B2,

    [PgName("C1")]
    C1,

    [PgName("C2")]
    C2,
}
