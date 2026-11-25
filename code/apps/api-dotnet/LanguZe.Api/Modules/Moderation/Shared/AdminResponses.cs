using System.Text.Json.Serialization;
using LanguZe.Api.Data.Entities;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>Which rule a blocked photo broke, as the admin area names it (FR-091).</summary>
internal enum BlockCategoryName
{
    [JsonStringEnumMemberName("SEXUAL_CONTENT")]
    SexualContent,

    [JsonStringEnumMemberName("SUSPECTED_ILLEGAL_MATERIAL")]
    SuspectedIllegalMaterial,

    [JsonStringEnumMemberName("VIOLENCE")]
    Violence,

    [JsonStringEnumMemberName("ILLEGAL_ACTIVITY")]
    IllegalActivity,

    [JsonStringEnumMemberName("HATE_SYMBOL")]
    HateSymbol,

    [JsonStringEnumMemberName("PERSONAL_DATA")]
    PersonalData,

    [JsonStringEnumMemberName("OTHER")]
    Other,
}

/// <summary>Why a learner's AI features were paused (FR-096, FR-098, V15).</summary>
internal enum SuspensionReasonName
{
    [JsonStringEnumMemberName("REPEATED_BLOCKS")]
    RepeatedBlocks,

    [JsonStringEnumMemberName("REPEATED_AFTER_SUSPENSION")]
    RepeatedAfterSuspension,

    [JsonStringEnumMemberName("SUSPECTED_ILLEGAL_MATERIAL")]
    SuspectedIllegalMaterial,
}

internal enum AccountStatusName
{
    [JsonStringEnumMemberName("ACTIVE")]
    Active,

    [JsonStringEnumMemberName("SUSPENDED")]
    Suspended,
}

/// <summary>How a learner can sign in (FR-003): <c>email</c> is a password, the rest are providers.</summary>
internal enum SignInMethodName
{
    [JsonStringEnumMemberName("email")]
    Email,

    [JsonStringEnumMemberName("google")]
    Google,

    [JsonStringEnumMemberName("line")]
    Line,

    [JsonStringEnumMemberName("facebook")]
    Facebook,
}

/// <summary>The database's names to the admin area's, member for member.</summary>
internal static class AdminNames
{
    public static BlockCategoryName Of(BlockCategory category) => Enum.Parse<BlockCategoryName>(category.ToString());

    public static SuspensionReasonName Of(SuspensionReason reason) => Enum.Parse<SuspensionReasonName>(reason.ToString());

    public static AccountStatusName Of(AccountStatus status) => status == AccountStatus.Suspended ? AccountStatusName.Suspended : AccountStatusName.Active;
}
