using System.Text.Json;
using System.Text.Json.Serialization;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Platform.OpenApi;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>What an admin can do to a learner's case (FR-105).</summary>
internal enum AdminActionName
{
    [JsonStringEnumMemberName("LIFT_AI_SUSPENSION")]
    LiftAiSuspension,

    [JsonStringEnumMemberName("SET_AI_SUSPENSION_END")]
    SetAiSuspensionEnd,

    [JsonStringEnumMemberName("SUSPEND_ACCOUNT")]
    SuspendAccount,

    [JsonStringEnumMemberName("REACTIVATE_ACCOUNT")]
    ReactivateAccount,

    [JsonStringEnumMemberName("DELETE_ACCOUNT")]
    DeleteAccount,
}

/// <summary>One admin action, as the audit log keeps it (AuditEntryDto; FR-106).</summary>
/// <param name="LearnerId">Kept after the account is deleted, and the only thing about the learner that is (FR-106).</param>
internal sealed record AuditEntryResponse(
    Guid Id,
    Guid AdminId,
    Guid LearnerId,
    AdminActionName Action,
    string Reason,
    [property: ApiPropertyOptional] AuditDetailsResponse? Details,
    DateTimeOffset CreatedAt);

/// <summary>What an action carried beyond its kind, kept with its audit entry (AuditDetailsDto).</summary>
/// <param name="EndsAt">The end date an admin set.</param>
/// <param name="PreviousEndsAt">The end date it replaced, empty when the suspension was waiting for review. Kept here because the suspension itself only remembers the new one.</param>
internal sealed record AuditDetailsResponse(
    [property: ApiPropertyOptional(Nullable = false)] string? EndsAt,
    [property: ApiPropertyOptional] string? PreviousEndsAt);

internal static class AuditEntries
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AuditEntryResponse ResponseOf(AdminAction row) =>
        new(
            row.Id,
            row.AdminId,
            row.LearnerId,
            Enum.Parse<AdminActionName>(row.Action.ToString()),
            row.Reason,
            row.Details is null ? null : JsonSerializer.Deserialize<AuditDetailsResponse>(row.Details, Json),
            row.CreatedAt);

    /// <summary>The details as the column keeps them, instants written as JavaScript's <c>toISOString</c> writes them.</summary>
    public static string DetailsOf(AuditDetailsResponse details) => JsonSerializer.Serialize(details, Json);
}
