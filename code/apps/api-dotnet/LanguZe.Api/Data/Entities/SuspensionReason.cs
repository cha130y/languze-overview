using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Why a learner's AI features were paused; the PostgreSQL enum <c>suspension_reason</c> (FR-096, FR-098, V15).</summary>
public enum SuspensionReason
{
    [PgName("REPEATED_BLOCKS")]
    RepeatedBlocks,

    [PgName("REPEATED_AFTER_SUSPENSION")]
    RepeatedAfterSuspension,

    [PgName("SUSPECTED_ILLEGAL_MATERIAL")]
    SuspectedIllegalMaterial,
}
