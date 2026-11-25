using System.Text.Json.Serialization;

namespace LanguZe.Api.Modules.Progress.Features.GetProgress;

/// <summary>Everything the progress page shows, field for field as NestJS's <c>ProgressDto</c> (FR-060, FR-061, US-060).</summary>
/// <param name="TotalXp">Only ever rises, whatever is deleted (V4).</param>
/// <param name="Worlds">Every world the learner has, newest first.</param>
internal sealed record ProgressResponse(int TotalXp, MasteryCountsResponse Words, IReadOnlyList<WorldProgressResponse> Worlds);

/// <summary>
/// How many of the learner's words stand at each level (FR-061). The keys are the levels
/// themselves; <c>NEW</c> is every word never answered, which has no mastery at all.
/// </summary>
internal sealed record MasteryCountsResponse(
    [property: JsonPropertyName("NEW")] int New,
    [property: JsonPropertyName("LEARNING")] int Learning,
    [property: JsonPropertyName("FAMILIAR")] int Familiar,
    [property: JsonPropertyName("MASTERED")] int Mastered);

/// <summary>One world's share of the learner's progress (FR-061).</summary>
internal sealed record WorldProgressResponse(Guid Id, string Name, int WordCount, int MasteredCount);
