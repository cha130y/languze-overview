using System.Text.Json.Serialization;
using LanguZe.Api.Platform.OpenApi;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>One world in the list (FR-013); NestJS's WorldSummaryDto, field for field.</summary>
/// <param name="FailureReason">Why the analysis failed, when it did.</param>
/// <param name="ThumbnailUrl">A signed link to the thumbnail, valid for a few minutes (P4). Empty when the photo is gone.</param>
/// <param name="WordCount">Words found in this world (FR-013).</param>
/// <param name="MasteredCount">Of those, the ones the learner has mastered.</param>
internal sealed record WorldSummaryResponse(
    Guid Id,
    string Name,
    WorldStatusName Status,
    [property: ApiPropertyOptional] string? FailureReason,
    [property: ApiPropertyOptional] string? ThumbnailUrl,
    int WordCount,
    int MasteredCount,
    DateTimeOffset CreatedAt);

/// <summary>One world with its photo and words (FR-014); NestJS's WorldDetailDto, which extends the summary.</summary>
/// <param name="FailureReason">Why the analysis failed, when it did.</param>
/// <param name="ThumbnailUrl">A signed link to the thumbnail, valid for a few minutes (P4). Empty when the photo is gone.</param>
/// <param name="WordCount">Words found in this world (FR-013).</param>
/// <param name="MasteredCount">Of those, the ones the learner has mastered.</param>
/// <param name="PhotoUrl">A signed link to the prepared photo (P4).</param>
internal sealed record WorldDetailResponse(
    Guid Id,
    string Name,
    WorldStatusName Status,
    [property: ApiPropertyOptional] string? FailureReason,
    [property: ApiPropertyOptional] string? ThumbnailUrl,
    int WordCount,
    int MasteredCount,
    DateTimeOffset CreatedAt,
    [property: ApiPropertyOptional] string? PhotoUrl,
    IReadOnlyList<WorldWordResponse> Words);

/// <summary>One word found in this world's photo (FR-014, FR-022).</summary>
/// <param name="Id">Identifies this word in this world (FR-026).</param>
/// <param name="Mastery">What the learner has to do with this word to master it.</param>
internal sealed record WorldWordResponse(
    Guid Id,
    string English,
    string ThaiMeaning,
    string ExampleSentence,
    CefrLevelName CefrLevel,
    HighlightBoxResponse Box,
    MasteryName Mastery);

/// <summary>A highlight box on the prepared photo, in 0–1 coordinates (AIR-003).</summary>
internal sealed record HighlightBoxResponse(double X, double Y, double Width, double Height);

/// <summary>Just the status, for the page that waits for an analysis (FR-021, P3).</summary>
internal sealed record WorldStatusResponse(WorldStatusName Status, [property: ApiPropertyOptional] string? FailureReason);

internal enum WorldStatusName
{
    [JsonStringEnumMemberName("ANALYZING")]
    Analyzing,

    [JsonStringEnumMemberName("READY")]
    Ready,

    [JsonStringEnumMemberName("FAILED")]
    Failed,
}

internal enum CefrLevelName
{
    A1,
    A2,
    B1,
    B2,
    C1,
    C2,
}

internal enum MasteryName
{
    [JsonStringEnumMemberName("NEW")]
    New,

    [JsonStringEnumMemberName("LEARNING")]
    Learning,

    [JsonStringEnumMemberName("FAMILIAR")]
    Familiar,

    [JsonStringEnumMemberName("MASTERED")]
    Mastered,
}
