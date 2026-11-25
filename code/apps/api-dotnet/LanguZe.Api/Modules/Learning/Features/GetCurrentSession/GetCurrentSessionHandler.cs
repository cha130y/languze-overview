using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Learning.Shared;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Learning.Features.GetCurrentSession;

/// <summary>
/// The session of that kind still open (sessions.service.ts, <c>current</c>; FR-036). A learner
/// who closed the tab comes back to the question they were on; one left for a day is over (V17).
/// </summary>
internal sealed class GetCurrentSessionHandler(LanguZeDbContext db, SessionReader reader)
{
    public async Task<SessionResponse?> HandleAsync(Guid learnerId, CurrentSessionQuery query, CancellationToken cancellationToken)
    {
        var open = db.PracticeSessions.Where(session => session.LearnerId == learnerId && session.Status == SessionStatus.InProgress);
        if (query.Kind == "GAME")
        {
            // The rules require a world for a game, so this is one by now.
            var worldId = Guid.Parse(query.WorldId!);
            open = open.Where(session => session.Kind == SessionKind.Game && session.WorldId == worldId);
        }
        else
        {
            open = open.Where(session => session.Kind == SessionKind.Review);
        }

        var found = await open.FirstOrDefaultAsync(cancellationToken);
        if (found is null || await reader.CloseIfStaleAsync(found, cancellationToken))
        {
            return null;
        }

        return await reader.ResponseOfAsync(found, cancellationToken);
    }
}
