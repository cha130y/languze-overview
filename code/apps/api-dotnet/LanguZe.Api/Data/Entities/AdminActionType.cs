using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>What an admin did to a learner's case (FR-105); the PostgreSQL enum <c>admin_action_type</c>.</summary>
public enum AdminActionType
{
    [PgName("LIFT_AI_SUSPENSION")]
    LiftAiSuspension,

    [PgName("SET_AI_SUSPENSION_END")]
    SetAiSuspensionEnd,

    [PgName("SUSPEND_ACCOUNT")]
    SuspendAccount,

    [PgName("REACTIVATE_ACCOUNT")]
    ReactivateAccount,

    [PgName("DELETE_ACCOUNT")]
    DeleteAccount,
}
