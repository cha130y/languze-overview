namespace LanguZe.Api.Modules.Learning.Domain;

/// <summary>One of a world's words, and how well the learner knows it; no level is <c>NEW</c> (FR-040).</summary>
public sealed record Candidate(Guid OccurrenceId, Guid VocabularyWordId, Stage? Level);

/// <summary>
/// The questions of one game, in the order asked (question-selection.ts; FR-030): the words
/// the learner knows least first, random within each group, and at most ten. Random order is
/// what keeps a world from becoming a sequence to memorize. <c>NEW</c> and <c>LEARNING</c>
/// share a place, as FR-030 states them.
/// </summary>
public static class QuestionSelection
{
    /// <summary>The most questions one session asks (FR-030, FR-050).</summary>
    public const int QuestionsPerSession = 10;

    /// <summary>The groups, least known first: NEW and LEARNING, then FAMILIAR, then MASTERED.</summary>
    private static readonly int[] Places = [0, 1, 2];

    public static IReadOnlyList<Candidate> Select(IReadOnlyList<Candidate> candidates, Func<IReadOnlyList<Candidate>, IReadOnlyList<Candidate>>? shuffle = null)
    {
        shuffle ??= items => ShuffleRandomly(items);
        return [.. Places
            .SelectMany(place => shuffle([.. candidates.Where(word => PlaceOf(word.Level) == place)]))
            .Take(QuestionsPerSession)];
    }

    private static int PlaceOf(Stage? level) => level switch
    {
        Stage.Familiar => 1,
        Stage.Mastered => 2,
        _ => 0,
    };

    /// <summary>Fisher–Yates: every order equally likely, which "random order" has to mean.</summary>
    private static Candidate[] ShuffleRandomly(IReadOnlyList<Candidate> items)
    {
        var shuffled = items.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }
}
