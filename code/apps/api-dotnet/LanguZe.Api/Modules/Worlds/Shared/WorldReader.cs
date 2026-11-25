using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Shared;

/// <summary>
/// Worlds as the API answers them (worlds.service.ts, <c>summaryOf</c> and <c>detailOf</c>):
/// each with its photos and words in one read, and the photos as links only this learner
/// can use, and only for a few minutes (P4, NFR-008).
/// </summary>
internal sealed class WorldReader(LanguZeDbContext db, IPhotoStorage storage)
{
    /// <summary>A world with everything the detail and the list need; the mastery comes with each word (FR-013, FR-014).</summary>
    public IQueryable<World> WithPhotosAndWords() =>
        db.Worlds
            .Include(world => world.Photo)
            .Include(world => world.Thumbnail)
            .Include(world => world.Occurrences.OrderBy(occurrence => occurrence.CreatedAt))
                .ThenInclude(occurrence => occurrence.VocabularyWord)
                    .ThenInclude(word => word.Mastery)
            .AsSplitQuery();

    /// <summary>
    /// List only the fields needed for each summary. This avoids materializing and tracking every
    /// occurrence, vocabulary word, and mastery row for up to twenty worlds (FR-013).
    /// </summary>
    public async Task<List<WorldSummaryResponse>> ListSummariesAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        var rows = await db.Worlds
            .AsNoTracking()
            .Where(world => world.LearnerId == learnerId)
            .OrderByDescending(world => world.CreatedAt)
            .Select(world => new WorldSummaryProjection(
                world.Id,
                world.Name,
                world.Status,
                world.FailureReason,
                world.Thumbnail == null ? null : world.Thumbnail.StorageKey,
                world.Occurrences.Count,
                world.Occurrences.Count(occurrence => occurrence.VocabularyWord.Mastery != null && occurrence.VocabularyWord.Mastery.Level == MasteryLevel.Mastered),
                world.CreatedAt))
            .ToListAsync(cancellationToken);

        var summaries = new List<WorldSummaryResponse>(rows.Count);
        foreach (var row in rows)
        {
            summaries.Add(new WorldSummaryResponse(
                row.Id,
                row.Name,
                StatusName(row.Status),
                FailureName(row.FailureReason),
                row.ThumbnailStorageKey is null ? null : await storage.SignedLinkAsync(row.ThumbnailStorageKey),
                row.WordCount,
                row.MasteredCount,
                row.CreatedAt));
        }

        return summaries;
    }

    /// <summary>
    /// The learner's own world, or nothing. Another learner's world answers NOT_FOUND rather
    /// than FORBIDDEN, so the API never confirms that it exists (FR-008).
    /// </summary>
    public async Task<World> OwnAsync(Guid learnerId, Guid worldId, CancellationToken cancellationToken) =>
        await WithPhotosAndWords().SingleOrDefaultAsync(world => world.Id == worldId && world.LearnerId == learnerId, cancellationToken)
        ?? throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "This world does not exist.");

    public async Task<WorldSummaryResponse> SummaryOfAsync(World world) =>
        new(
            world.Id,
            world.Name,
            StatusName(world.Status),
            FailureName(world.FailureReason),
            await LinkToAsync(world.Thumbnail),
            world.Occurrences.Count,
            world.Occurrences.Count(occurrence => occurrence.VocabularyWord.Mastery?.Level == MasteryLevel.Mastered),
            world.CreatedAt);

    public async Task<WorldDetailResponse> DetailOfAsync(World world)
    {
        var summary = await SummaryOfAsync(world);
        return new WorldDetailResponse(
            summary.Id,
            summary.Name,
            summary.Status,
            summary.FailureReason,
            summary.ThumbnailUrl,
            summary.WordCount,
            summary.MasteredCount,
            summary.CreatedAt,
            await LinkToAsync(world.Photo),
            [.. world.Occurrences.Select(occurrence => new WorldWordResponse(
                occurrence.Id,
                occurrence.VocabularyWord.English,
                // This photo's meaning: one English word can mean another thing elsewhere (FR-043).
                occurrence.ThaiMeaning,
                occurrence.ExampleSentence,
                (CefrLevelName)occurrence.CefrLevel,
                new HighlightBoxResponse(occurrence.BoxX, occurrence.BoxY, occurrence.BoxWidth, occurrence.BoxHeight),
                // A word with no mastery row has never been answered, which is NEW.
                occurrence.VocabularyWord.Mastery?.Level switch
                {
                    MasteryLevel.Learning => MasteryName.Learning,
                    MasteryLevel.Familiar => MasteryName.Familiar,
                    MasteryLevel.Mastered => MasteryName.Mastered,
                    _ => MasteryName.New,
                }))]);
    }

    public static WorldStatusName StatusName(WorldStatus status) => status switch
    {
        WorldStatus.Ready => WorldStatusName.Ready,
        WorldStatus.Failed => WorldStatusName.Failed,
        _ => WorldStatusName.Analyzing,
    };

    /// <summary>The reason as analysis_failure spells it.</summary>
    public static string? FailureName(AnalysisFailure? reason) => reason switch
    {
        AnalysisFailure.Blocked => "BLOCKED",
        AnalysisFailure.TooFewWords => "TOO_FEW_WORDS",
        AnalysisFailure.ProviderError => "PROVIDER_ERROR",
        AnalysisFailure.InvalidOutput => "INVALID_OUTPUT",
        AnalysisFailure.TimedOut => "TIMED_OUT",
        _ => null,
    };

    private async Task<string?> LinkToAsync(StoredPhoto? photo) =>
        photo is null ? null : await storage.SignedLinkAsync(photo.StorageKey);

    private sealed record WorldSummaryProjection(
        Guid Id,
        string Name,
        WorldStatus Status,
        AnalysisFailure? FailureReason,
        string? ThumbnailStorageKey,
        int WordCount,
        int MasteredCount,
        DateTimeOffset CreatedAt);
}
