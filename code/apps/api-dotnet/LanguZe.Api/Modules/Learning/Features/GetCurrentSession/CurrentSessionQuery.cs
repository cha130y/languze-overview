using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Learning.Features.GetCurrentSession;

/// <summary>Reading the open session of one kind (CurrentSessionQuery). A review has no world (FR-050).</summary>
/// <param name="WorldId">Required for a game.</param>
internal sealed record CurrentSessionQuery(
    [property: IsIn("GAME", "REVIEW")] string Kind,
    [property: ValidateIfEquals("kind", "GAME"), IsUuid] string? WorldId);
