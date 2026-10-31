using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>What an account may do; the PostgreSQL enum <c>user_role</c>. Only the promotion script makes an admin (FR-101).</summary>
public enum UserRole
{
    [PgName("LEARNER")]
    Learner,

    [PgName("ADMIN")]
    Admin,
}
