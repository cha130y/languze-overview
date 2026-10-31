using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Worlds.Features.RenameWorld;

/// <summary>NestJS's RenameWorldDto: 1 to 50 characters, the rule wherever a world is named (V3, FR-016).</summary>
public sealed record RenameWorldRequest([property: IsString, Length(1, 50), ApiProperty(MinLength = 1, MaxLength = 50)] string Name);
