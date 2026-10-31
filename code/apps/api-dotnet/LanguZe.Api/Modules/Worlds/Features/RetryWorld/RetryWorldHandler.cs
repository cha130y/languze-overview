using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Features.RetryWorld;

/// <summary>
/// Analyses a failed world's photo again (worlds.service.ts and analysis.service.ts,
/// <c>retry</c>; FR-025, US-021). A retry is an analysis like any other, so it needs the same
/// access as creating a world, checked first: a paused learner may still have a photo from
/// before the pause (FR-096). Only a failed world with a photo can be retried: a blocked
/// photo is gone, so its world can only be deleted.
/// </summary>
internal sealed class RetryWorldHandler(LanguZeDbContext db, IAiVerification verification, IAiAccess aiAccess, Analyses analyses, AnalysisRunner runner)
{
    public async Task HandleAsync(Guid learnerId, Guid worldId, string requestId, CancellationToken cancellationToken)
    {
        await aiAccess.RequireAsync(
            learnerId,
            await verification.IsVerifiedForAiAsync(learnerId, cancellationToken),
            "Verify your email address to analyse photos.",
            cancellationToken);

        var world = await db.Worlds.Include(row => row.Photo)
            .SingleOrDefaultAsync(row => row.Id == worldId && row.LearnerId == learnerId, cancellationToken)
            ?? throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "This world does not exist.");
        if (world.Status != WorldStatus.Failed || world.Photo is null)
        {
            throw new AppException(
                ErrorCodes.RetryNotAvailable,
                StatusCodes.Status409Conflict,
                world.Photo is not null ? "This world has nothing to retry." : "This photo was removed, so the world can only be deleted.");
        }

        var analysisId = await analyses.TakeAsync(learnerId, worldId, cancellationToken);
        world.Status = WorldStatus.Analyzing;
        world.FailureReason = null;
        await db.SaveChangesAsync(cancellationToken);
        runner.Start(analysisId, worldId, world.Photo.StorageKey, requestId);
    }
}
