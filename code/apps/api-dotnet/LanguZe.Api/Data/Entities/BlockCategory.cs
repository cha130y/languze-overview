using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Which rule a blocked photo broke (FR-091); the PostgreSQL enum <c>block_category</c>.</summary>
public enum BlockCategory
{
    [PgName("SEXUAL_CONTENT")]
    SexualContent,

    [PgName("SUSPECTED_ILLEGAL_MATERIAL")]
    SuspectedIllegalMaterial,

    [PgName("VIOLENCE")]
    Violence,

    [PgName("ILLEGAL_ACTIVITY")]
    IllegalActivity,

    [PgName("HATE_SYMBOL")]
    HateSymbol,

    [PgName("PERSONAL_DATA")]
    PersonalData,

    [PgName("OTHER")]
    Other,
}
