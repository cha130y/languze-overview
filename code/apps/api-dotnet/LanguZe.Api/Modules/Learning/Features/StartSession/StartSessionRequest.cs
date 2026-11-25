using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Learning.Features.StartSession;

/// <summary>
/// Starting a session (StartSessionDto). A game is of one world; a review draws from all of them
/// by a rule of its own (FR-030, FR-050), so it names no world.
/// </summary>
/// <param name="WorldId">The world to play; required for a game only (FR-030).</param>
internal sealed record StartSessionRequest(
    [property: IsIn("GAME", "REVIEW"), ApiProperty(Enum = ["GAME", "REVIEW"])] string Kind,
    [property: ValidateIfEquals("kind", "GAME"), IsUuid, ApiPropertyOptional(Nullable = false)] string? WorldId);
