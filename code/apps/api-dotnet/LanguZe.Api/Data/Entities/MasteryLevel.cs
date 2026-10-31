using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>How far a learner has got with a word; no row at all means new (FR-040); the PostgreSQL enum <c>mastery_level</c>.</summary>
public enum MasteryLevel
{
    [PgName("LEARNING")]
    Learning,

    [PgName("FAMILIAR")]
    Familiar,

    [PgName("MASTERED")]
    Mastered,
}
