using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Text;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Moderation.Features.ActOnLearner;

/// <summary>One action on one learner, and why (AdminActionDto; FR-105, US-104).</summary>
/// <param name="Reason">Why, in 1 to 500 characters. An appeal received by email is noted here; appeals have no field of their own (US-104 criterion 8).</param>
/// <param name="EndsAt">For SET_AI_SUSPENSION_END, and only for it: when AI features come back. Must be in the future.</param>
internal sealed record ActRequest(
    [property: IsIn("LIFT_AI_SUSPENSION", "SET_AI_SUSPENSION_END", "SUSPEND_ACCOUNT", "REACTIVATE_ACCOUNT", "DELETE_ACCOUNT"), ApiProperty(Enum = ["LIFT_AI_SUSPENSION", "SET_AI_SUSPENSION_END", "SUSPEND_ACCOUNT", "REACTIVATE_ACCOUNT", "DELETE_ACCOUNT"])] string Action,
    // Something other than spaces: the audit log is only as good as its reasons.
    [property: IsString, Matches("[^" + JsText.WhiteSpace + "]"), MaxLength(ActOnLearnerHandler.MaxReasonLength), ApiProperty(MinLength = 1, MaxLength = ActOnLearnerHandler.MaxReasonLength)] string Reason,
    [property: IsOptional, IsIso8601, ApiPropertyOptional(Nullable = false)] string? EndsAt);
