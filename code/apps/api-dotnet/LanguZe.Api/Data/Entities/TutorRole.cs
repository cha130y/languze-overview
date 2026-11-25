using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Who said a message in a tutor conversation (FR-070); the PostgreSQL enum <c>tutor_role</c>.</summary>
public enum TutorRole
{
    [PgName("LEARNER")]
    Learner,

    [PgName("TUTOR")]
    Tutor,
}
