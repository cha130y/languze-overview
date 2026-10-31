using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>Raises a pause from the learner's blocks and earlier suspensions, as SuspensionService.raiseFor does.</summary>
internal sealed class SuspensionRaiser(LanguZeDbContext db, TimeProvider clock) : ISuspensions
{
    public async Task<RaisedSuspension?> RaiseForAsync(Guid learnerId, BlockCategory category, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var windowFrom = SuspensionRules.BlockWindowFrom(now);
        var blocksInWindow = await db.PhotoBlocks.CountAsync(block => block.LearnerId == learnerId && block.CreatedAt >= windowFrom, cancellationToken);
        var suspendedBefore = await db.AiSuspensions.AnyAsync(suspension => suspension.LearnerId == learnerId, cancellationToken);

        var raise = SuspensionRules.For(
            new BlockOutcome(category == BlockCategory.SuspectedIllegalMaterial, blocksInWindow, suspendedBefore),
            now);
        if (raise is null)
        {
            return null;
        }

        var suspension = new AiSuspension
        {
            LearnerId = learnerId,
            Reason = raise.Reason switch
            {
                PauseReason.RepeatedBlocks => SuspensionReason.RepeatedBlocks,
                PauseReason.RepeatedAfterSuspension => SuspensionReason.RepeatedAfterSuspension,
                _ => SuspensionReason.SuspectedIllegalMaterial,
            },
            StartsAt = now,
            EndsAt = raise.EndsAt,
        };
        db.AiSuspensions.Add(suspension);
        await db.SaveChangesAsync(cancellationToken);
        return new RaisedSuspension(suspension.Id, suspension.EndsAt);
    }
}
