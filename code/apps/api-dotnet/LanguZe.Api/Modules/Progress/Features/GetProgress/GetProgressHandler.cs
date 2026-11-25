using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Progress.Features.GetProgress;

/// <summary>
/// What the learner has to show for their practice (progress.service.ts; FR-060, FR-061).
/// Everything is counted from the rows that exist; only XP is kept as a running total, in a
/// table of its own, so deleting a world never takes points away (V4).
/// </summary>
internal sealed class GetProgressHandler(LanguZeDbContext db)
{
    public async Task<ProgressResponse> HandleAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        var totalXp = await db.LearnerXp.Where(xp => xp.LearnerId == learnerId).Select(xp => (int?)xp.TotalXp).SingleOrDefaultAsync(cancellationToken) ?? 0;
        var totalWords = await db.VocabularyWords.CountAsync(word => word.LearnerId == learnerId, cancellationToken);
        var byLevel = await db.WordMastery.Where(mastery => mastery.LearnerId == learnerId)
            .GroupBy(mastery => mastery.Level)
            .Select(group => new { Level = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Level, group => group.Count, cancellationToken);
        var worlds = await db.Worlds.Where(world => world.LearnerId == learnerId)
            .OrderByDescending(world => world.CreatedAt)
            .Select(world => new WorldProgressResponse(
                world.Id,
                world.Name,
                world.Occurrences.Count,
                world.Occurrences.Count(occurrence => occurrence.VocabularyWord.Mastery != null && occurrence.VocabularyWord.Mastery.Level == MasteryLevel.Mastered)))
            .ToListAsync(cancellationToken);

        int Counted(MasteryLevel level) => byLevel.GetValueOrDefault(level);

        // A word has a mastery only once it has been answered, so the new ones are whatever is
        // left over; counting them another way would mean writing learning data during analysis.
        return new ProgressResponse(
            totalXp,
            new MasteryCountsResponse(totalWords - byLevel.Values.Sum(), Counted(MasteryLevel.Learning), Counted(MasteryLevel.Familiar), Counted(MasteryLevel.Mastered)),
            worlds);
    }
}
