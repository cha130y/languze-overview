using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Features.DeleteWorld;

/// <summary>
/// Deletes a world and leaves its photos for the cleanup task (worlds.service.ts, <c>remove</c>;
/// FR-015, NFR-009). The rows and the cleanup records are written together, so a photo can
/// never be orphaned in storage: the record exists exactly when a file still has to go.
/// </summary>
internal sealed class DeleteWorldHandler(LanguZeDbContext db, WorldReader reader, IPhotoCleanup photoCleanup)
{
    public async Task HandleAsync(Guid learnerId, Guid worldId, CancellationToken cancellationToken)
    {
        var world = await reader.OwnAsync(learnerId, worldId, cancellationToken);
        var photos = new[] { world.Photo, world.Thumbnail }.OfType<StoredPhoto>().ToList();
        var photoIds = photos.Select(photo => photo.Id).ToList();

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ChangeTracker.Clear();
            db.PhotoDeletions.AddRange(photos.Select(photo => new PhotoDeletion { StorageKey = photo.StorageKey, Reason = PhotoDeletionReason.WorldDeleted }));
            await db.SaveChangesAsync(cancellationToken);
            await db.Worlds.Where(row => row.Id == worldId).ExecuteDeleteAsync(cancellationToken);

            // A word exists because a photo had it in view. Once its last occurrence goes with
            // the world, so does the word, and with it the mastery and the mistakes it collected
            // (FR-015, US-014 criterion 3).
            await db.VocabularyWords
                .Where(word => word.LearnerId == learnerId && !db.WordOccurrences.Any(occurrence => occurrence.VocabularyWordId == word.Id))
                .ExecuteDeleteAsync(cancellationToken);
            await db.StoredPhotos.Where(photo => photoIds.Contains(photo.Id)).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        photoCleanup.Expect();
    }
}
