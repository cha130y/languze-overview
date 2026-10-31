using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Which job an AI call did (AIR-007); the PostgreSQL enum <c>ai_purpose</c>.</summary>
public enum AiPurpose
{
    [PgName("SAFETY_CHECK")]
    SafetyCheck,

    [PgName("EXTRACTION")]
    Extraction,

    [PgName("TUTOR")]
    Tutor,
}
