namespace LanguZe.Api.Modules.Learning.Domain;

/// <summary>One word a review could ask about, with what decides its place.</summary>
/// <param name="OccurrenceId">From the learner's most recently created world that has the word (FR-051).</param>
/// <param name="LastPractisedAt">When the word was last answered, right or wrong.</param>
/// <param name="LastMistakeAt">When it was last got wrong; none if it never was.</param>
public sealed record ReviewCandidate(Guid VocabularyWordId, Guid OccurrenceId, Stage Level, DateTimeOffset LastPractisedAt, DateTimeOffset? LastMistakeAt);

/// <summary>
/// Which words a review asks about, in the order it asks them (review-selection.ts; FR-050):
/// words got wrong, the latest mistake first; then <c>FAMILIAR</c> words, least recently
/// practised first; then <c>LEARNING</c> words, the same. Nothing is random, unlike a game: the
/// order is the feature. <c>MASTERED</c> words and words never answered are not candidates,
/// since review repairs what is half-known. Words that tie keep the order they came in, as
/// JavaScript's sort does.
/// </summary>
public static class ReviewSelection
{
    public static IReadOnlyList<ReviewCandidate> Select(IReadOnlyList<ReviewCandidate> all, int limit)
    {
        var candidates = all.Where(word => word.Level != Stage.Mastered).ToList();
        var mistaken = candidates.Where(word => word.LastMistakeAt is not null).OrderByDescending(word => word.LastMistakeAt);

        IEnumerable<ReviewCandidate> ByLevel(Stage level) =>
            candidates.Where(word => word.LastMistakeAt is null && word.Level == level).OrderBy(word => word.LastPractisedAt);

        return [.. mistaken.Concat(ByLevel(Stage.Familiar)).Concat(ByLevel(Stage.Learning)).Take(limit)];
    }
}
