using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Moderation.Features.FindLearner;

/// <summary>
/// Finding one learner, by the exact address they wrote from or the account ID they read out of
/// their settings (FindLearnerQuery; FR-108, US-106). Exactly one of the two: each is checked
/// for shape here, and that only one was given where the search runs, since a field cannot see
/// its neighbour.
/// </summary>
/// <param name="Email">The whole address, matched exactly. A placeholder address finds nothing: learners without an email address are found by account ID (D1).</param>
internal sealed record FindLearnerQuery(
    [property: IsOptional, IsString, IsNotEmpty, MaxLength(254), ApiProperty(MaxLength = 254)] string? Email,
    [property: IsOptional, IsUuid] string? AccountId);
