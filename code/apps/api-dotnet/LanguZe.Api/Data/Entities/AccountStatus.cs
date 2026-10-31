using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Whether an account may sign in; the PostgreSQL enum <c>account_status</c>. Moderation suspends (FR-105).</summary>
public enum AccountStatus
{
    [PgName("ACTIVE")]
    Active,

    [PgName("SUSPENDED")]
    Suspended,
}
