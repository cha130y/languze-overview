using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Worlds.Features.CreateWorld;

/// <summary>NestJS's CreateWorldDto, the form's one text field; the photo comes as its file.</summary>
/// <param name="Name">What the learner calls this place.</param>
public sealed record CreateWorldRequest([property: IsString, Length(1, 50), ApiProperty(MinLength = 1, MaxLength = 50)] string Name);
