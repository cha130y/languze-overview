using LanguZe.Api.Modules.Learning.Domain;
using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Learning.Features.AnswerQuestion;

/// <summary>
/// One answer (AnswerDto): a typed word, or "I don't know". An empty answer is neither, and is
/// refused rather than recorded as a mistake the learner did not make (UC-031 1b).
/// </summary>
/// <param name="DontKnow">The learner gave up on this word.</param>
internal sealed record AnswerRequest(
    [property: ValidateIfNotTrue("dontKnow"), IsString, Length(1, AnswerCheck.MaxAnswerLength), ApiPropertyOptional(Nullable = false), ApiProperty(MaxLength = AnswerCheck.MaxAnswerLength)] string? Answer,
    [property: IsOptional, IsBoolean, ApiPropertyOptional(Nullable = false)] bool? DontKnow);
