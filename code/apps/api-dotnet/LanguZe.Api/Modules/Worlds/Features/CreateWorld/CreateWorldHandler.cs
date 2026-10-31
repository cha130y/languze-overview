using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Config;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Validation;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Features.CreateWorld;

/// <summary>
/// Creates a world from an uploaded photo (worlds.service.ts, <c>create</c>; FR-010–FR-012,
/// FR-017), in NestJS's order: the form, its name, the photo's presence, access to the AI
/// features, room for another world, the photo itself, and one of today's analyses. The
/// prepared files go to storage before the rows exist, because a row pointing at a file that
/// failed to upload would be a broken world; if the rows then fail, the files are recorded
/// for cleanup rather than left behind (NFR-009).
/// </summary>
internal sealed class CreateWorldHandler(
    LanguZeDbContext db,
    IAiVerification verification,
    IAiAccess aiAccess,
    PhotoPreparation preparation,
    IPhotoStorage storage,
    IPhotoCleanup photoCleanup,
    Analyses analyses,
    WorldReader reader,
    UsageLimits limits)
{
    public async Task<WorldDetailResponse> HandleAsync(Guid learnerId, HttpContext context, CancellationToken cancellationToken)
    {
        var form = await PhotoForm.ReadAsync(context.Request, cancellationToken);
        var request = RequestBody.Check<CreateWorldRequest>(form.Fields, context);
        if (form.Photo is null)
        {
            throw new AppException(
                ErrorCodes.ValidationFailed,
                StatusCodes.Status400BadRequest,
                "A photo is required.",
                new Dictionary<string, object?> { ["fields"] = new Dictionary<string, string> { ["photo"] = "A photo is required." } });
        }

        // Photo analysis is an AI feature, so it needs a verified account whose AI features are not paused (FR-006, FR-096).
        await aiAccess.RequireAsync(
            learnerId,
            await verification.IsVerifiedForAiAsync(learnerId, cancellationToken),
            "Verify your email address to analyse photos.",
            cancellationToken);
        await RequireRoomAsync(learnerId, cancellationToken);

        var prepared = preparation.Prepare(form.Photo);

        // Taken before anything is stored, so a learner who has used today's analyses is
        // refused with nothing left behind (FR-020); given back below if the world fails.
        var analysisId = await analyses.TakeAsync(learnerId, null, cancellationToken);
        var photoKey = StorageKeys.New(PhotoKind.Prepared);
        var thumbnailKey = StorageKeys.New(PhotoKind.Thumbnail);
        await storage.StoreAsync(new PhotoUpload(photoKey, prepared.Prepared.Data, prepared.Prepared.ContentType), cancellationToken);
        await storage.StoreAsync(new PhotoUpload(thumbnailKey, prepared.Thumbnail.Data, prepared.Thumbnail.ContentType), cancellationToken);

        try
        {
            var world = new World
            {
                LearnerId = learnerId,
                Name = request.Name,
                Photo = Row(learnerId, photoKey, PhotoKind.Prepared, prepared.Prepared),
                Thumbnail = Row(learnerId, thumbnailKey, PhotoKind.Thumbnail, prepared.Thumbnail),
            };
            db.Worlds.Add(world);
            await db.SaveChangesAsync(cancellationToken);

            // The learner is answered now; the words arrive later (P2, FR-021).
            await analyses.BeginAsync(analysisId, world.Id, photoKey, context.TraceIdentifier, cancellationToken);
            return await reader.DetailOfAsync(await reader.WithPhotosAndWords().SingleAsync(row => row.Id == world.Id, cancellationToken));
        }
        catch
        {
            await analyses.ReleaseAsync(analysisId);
            db.ChangeTracker.Clear();
            db.PhotoDeletions.AddRange(
                new PhotoDeletion { StorageKey = photoKey, Reason = PhotoDeletionReason.UploadFailed },
                new PhotoDeletion { StorageKey = thumbnailKey, Reason = PhotoDeletionReason.UploadFailed });
            await db.SaveChangesAsync(CancellationToken.None);
            photoCleanup.Expect();
            throw;
        }
    }

    private async Task RequireRoomAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        if (await db.Worlds.CountAsync(world => world.LearnerId == learnerId, cancellationToken) < limits.Worlds)
        {
            return;
        }

        throw new AppException(
            ErrorCodes.WorldLimitReached,
            StatusCodes.Status409Conflict,
            $"A learner can have {limits.Worlds} worlds. Delete one to make room.",
            new Dictionary<string, object?> { ["limit"] = limits.Worlds });
    }

    private static StoredPhoto Row(Guid ownerId, string key, PhotoKind kind, PreparedImage image) => new()
    {
        OwnerId = ownerId,
        StorageKey = key,
        Kind = kind,
        ContentType = image.ContentType,
        Width = image.Width,
        Height = image.Height,
        ByteSize = image.ByteSize,
    };
}
