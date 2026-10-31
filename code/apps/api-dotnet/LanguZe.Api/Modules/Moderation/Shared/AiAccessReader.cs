using LanguZe.Api.Data;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>A learner's AI access from their suspensions, as SuspensionService.accessFor reads it.</summary>
internal sealed class AiAccessReader(LanguZeDbContext db, TimeProvider clock) : IAiAccess
{
    public async Task<AiAccess> ForAsync(Guid learnerId, bool verifiedForAi, CancellationToken cancellationToken)
    {
        // Lifted ones are left out to read less; SuspensionRules decides what counts either way.
        var open = await db.AiSuspensions
            .Where(suspension => suspension.LearnerId == learnerId && suspension.LiftedAt == null)
            .OrderByDescending(suspension => suspension.StartsAt)
            .Select(suspension => new SuspensionRecord(suspension.StartsAt, suspension.EndsAt, suspension.LiftedAt))
            .ToListAsync(cancellationToken);

        return SuspensionRules.Access(verifiedForAi, SuspensionRules.Current(open, clock.GetUtcNow()));
    }
}
