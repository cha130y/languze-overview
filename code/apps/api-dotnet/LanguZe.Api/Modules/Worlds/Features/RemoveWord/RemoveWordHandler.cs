using LanguZe.Api.Data;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Features.RemoveWord;

/// <summary>
/// Removes one word from a world (worlds.service.ts, <c>removeWord</c>; FR-026, FR-027). The
/// last word cannot go: a world with no words is nothing to practise, so the learner deletes
/// it instead (V5). The word itself goes when this was its last occurrence, taking its
/// mastery and mistakes with it.
/// </summary>
internal sealed class RemoveWordHandler(LanguZeDbContext db, WorldReader reader)
{
    public async Task HandleAsync(Guid learnerId, Guid worldId, Guid occurrenceId, CancellationToken cancellationToken)
    {
        await reader.OwnAsync(learnerId, worldId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var occurrence = await db.WordOccurrences.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == occurrenceId && row.WorldId == worldId, cancellationToken)
            ?? throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "This word is not in this world.");

        if (await db.WordOccurrences.CountAsync(row => row.WorldId == worldId, cancellationToken) <= 1)
        {
            throw new AppException(ErrorCodes.LastWord, StatusCodes.Status409Conflict, "A world keeps at least one word. Delete the world instead.");
        }

        await db.WordOccurrences.Where(row => row.Id == occurrenceId).ExecuteDeleteAsync(cancellationToken);
        if (!await db.WordOccurrences.AnyAsync(row => row.VocabularyWordId == occurrence.VocabularyWordId, cancellationToken))
        {
            await db.VocabularyWords.Where(word => word.Id == occurrence.VocabularyWordId).ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
