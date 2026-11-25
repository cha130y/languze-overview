using LanguZe.Api.Data;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Learning.Features.GetSession;

/// <summary>
/// One session (sessions.service.ts, <c>get</c>). A session left for a day is closed on the way,
/// and, as NestJS answers, this answer still shows it as it was read: the next read shows it
/// abandoned (V17).
/// </summary>
internal sealed class GetSessionHandler(LanguZeDbContext db, SessionReader reader)
{
    public async Task<SessionResponse> HandleAsync(Guid learnerId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.PracticeSessions.FirstOrDefaultAsync(row => row.Id == sessionId && row.LearnerId == learnerId, cancellationToken)
            ?? throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "This session does not exist.");

        await reader.CloseIfStaleAsync(session, cancellationToken);
        return await reader.ResponseOfAsync(session, cancellationToken);
    }
}
