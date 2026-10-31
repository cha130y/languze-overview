using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Vocabulary.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>
/// One analysis of a world's photo, from the safety check to the saved words (analysis.service.ts,
/// <c>run</c>, <c>save</c>, <c>blocked</c> and <c>failed</c>; FR-020–FR-025). It owns the analysis row it was
/// given, checks that the world still exists before saving anything, and every ending is one
/// transaction.
/// </summary>
internal sealed class AnalysisWork(
    LanguZeDbContext db,
    IPhotoStorage storage,
    IAiProvider ai,
    AiCallRecorder recorder,
    ISuspensions suspensions,
    IReviewNotifier review,
    IPhotoCleanup photoCleanup,
    TimeProvider clock)
{
    public async Task RunAsync(Guid analysisId, Guid worldId, string storageKey, string requestId)
    {
        var photo = new PhotoForAi(await storage.ReadAsync(storageKey, CancellationToken.None), "image/jpeg");

        // A provider that cannot answer is not a block: the learner did nothing wrong.
        var verdict = await recorder.RecordAsync(
            new AiCallDetails(AiPurpose.SafetyCheck, ai.Name, ai.ModelFor(AiPurpose.SafetyCheck)),
            requestId,
            () => ai.CheckPhotoAsync(photo, CancellationToken.None));
        if (!verdict.Allowed)
        {
            await BlockedAsync(analysisId, worldId, verdict.Category ?? throw new InvalidOperationException("A blocked verdict names its category."));
            return;
        }

        var extracted = await recorder.RecordAsync(
            new AiCallDetails(AiPurpose.Extraction, ai.Name, ai.ModelFor(AiPurpose.Extraction)),
            requestId,
            () => ai.ExtractVocabularyAsync(photo, CancellationToken.None));
        var items = ExtractionRules.CheckedItems(extracted);

        if (items.Count == 0)
        {
            throw new AnalysisFailedException(AnalysisFailure.InvalidOutput);
        }

        if (items.Count < ExtractionRules.MinWordsPerWorld)
        {
            throw new AnalysisFailedException(AnalysisFailure.TooFewWords);
        }

        await SaveAsync(analysisId, worldId, items);
    }

    /// <summary>A failure of LanguZe or its provider: the analysis is released (FR-025).</summary>
    public async Task FailedAsync(Guid analysisId, Guid worldId, AnalysisFailure reason)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Worlds.Where(world => world.Id == worldId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(world => world.Status, WorldStatus.Failed)
                .SetProperty(world => world.FailureReason, reason)
                .SetProperty(world => world.UpdatedAt, now));
        await db.Analyses.Where(analysis => analysis.Id == analysisId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(analysis => analysis.Status, AnalysisStatus.Failed)
                .SetProperty(analysis => analysis.FailureReason, reason)
                .SetProperty(analysis => analysis.FinishedAt, now));
        await transaction.CommitAsync();
    }

    /// <summary>
    /// A photo that broke the rules (FR-092–FR-095). It is recorded, its world keeps no photo,
    /// and the file is removed from storage straight after the transaction; if that fails, the
    /// cleanup record keeps it on the list.
    /// </summary>
    private async Task BlockedAsync(Guid analysisId, Guid worldId, BlockCategory category)
    {
        var world = await db.Worlds.AsNoTracking().Include(row => row.Photo).Include(row => row.Thumbnail)
            .SingleOrDefaultAsync(row => row.Id == worldId);
        if (world is null)
        {
            return;
        }

        var photos = new[] { world.Photo, world.Thumbnail }.OfType<StoredPhoto>().ToList();
        var photoIds = photos.Select(photo => photo.Id).ToList();
        var now = clock.GetUtcNow();
        RaisedSuspension? raised;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.PhotoBlocks.Add(new PhotoBlock { LearnerId = world.LearnerId, Category = category, CreatedAt = now });
            await db.SaveChangesAsync();

            // The pause this block has earned, decided here so the count it reads already
            // includes this photo and two blocks landing together cannot each decide they were
            // the second (FR-096, FR-098, moderation flow, section 1).
            raised = await suspensions.RaiseForAsync(world.LearnerId, category, CancellationToken.None);
            db.PhotoDeletions.AddRange(photos.Select(photo => new PhotoDeletion { StorageKey = photo.StorageKey, Reason = PhotoDeletionReason.PhotoBlocked }));
            await db.SaveChangesAsync();
            await db.StoredPhotos.Where(photo => photoIds.Contains(photo.Id)).ExecuteDeleteAsync();
            await db.Worlds.Where(row => row.Id == worldId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, WorldStatus.Failed)
                    .SetProperty(row => row.FailureReason, AnalysisFailure.Blocked)
                    .SetProperty(row => row.UpdatedAt, now));

            // A blocked photo counts toward the daily limit (FR-093).
            await db.Analyses.Where(analysis => analysis.Id == analysisId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(analysis => analysis.Status, AnalysisStatus.Blocked)
                    .SetProperty(analysis => analysis.FailureReason, AnalysisFailure.Blocked)
                    .SetProperty(analysis => analysis.FinishedAt, now));
            await transaction.CommitAsync();
        }

        // After the commit, never inside it: a slow mail server must not hold a transaction
        // open, and an email about a pause that was then rolled back would be worse than none
        // (FR-107, moderation flow, section 1).
        if (raised is not null)
        {
            await review.CaseNeedsReviewAsync(raised, CancellationToken.None);
        }

        // The records stay until the cleanup task has confirmed each file is gone.
        photoCleanup.Expect();
        await Task.WhenAll(photos.Select(async photo =>
        {
            try
            {
                await storage.RemoveAsync(photo.StorageKey, CancellationToken.None);
            }
            catch (Exception)
            {
                // The cleanup record already has it; the task will try again.
            }
        }));
    }

    /// <summary>The words a learner gets, the world, and the analysis: all or nothing.</summary>
    private async Task SaveAsync(Guid analysisId, Guid worldId, IReadOnlyList<CheckedItem> items)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = clock.GetUtcNow();
        var analysis = await db.Analyses.SingleAsync(row => row.Id == analysisId);
        var world = await db.Worlds.SingleOrDefaultAsync(row => row.Id == worldId);
        if (world is not null)
        {
            foreach (var item in items)
            {
                var word = await db.VocabularyWords.SingleOrDefaultAsync(row =>
                    row.LearnerId == world.LearnerId && row.English == item.English && row.ThaiMeaningKey == item.ThaiMeaningKey);
                if (word is null)
                {
                    word = new VocabularyWord
                    {
                        LearnerId = world.LearnerId,
                        English = item.English,
                        ThaiMeaning = item.ThaiMeaning,
                        ThaiMeaningKey = item.ThaiMeaningKey,
                    };
                    db.VocabularyWords.Add(word);
                    await db.SaveChangesAsync();
                }

                db.WordOccurrences.Add(new WordOccurrence
                {
                    WorldId = worldId,
                    VocabularyWordId = word.Id,
                    BoxX = item.Box.X,
                    BoxY = item.Box.Y,
                    BoxWidth = item.Box.Width,
                    BoxHeight = item.Box.Height,
                    ExampleSentence = item.ExampleSentence,
                    CefrLevel = item.CefrLevel,
                    AcceptedVariants = [.. item.AcceptedVariants],
                });
                await db.SaveChangesAsync();
            }

            world.Status = WorldStatus.Ready;
            world.FailureReason = null;
        }

        // When the learner deleted the world while this ran, the result is dropped.
        analysis.Status = AnalysisStatus.Succeeded;
        analysis.FinishedAt = now;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
