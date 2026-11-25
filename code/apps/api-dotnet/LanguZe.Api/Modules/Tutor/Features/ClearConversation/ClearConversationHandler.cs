using LanguZe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Tutor.Features.ClearConversation;

/// <summary>
/// Deletes the learner's messages (tutor.service.ts, <c>clear</c>; FR-076). Today's count is
/// left alone: the messages were sent, and forgetting them does not unsend them (US-073).
/// </summary>
internal sealed class ClearConversationHandler(LanguZeDbContext db)
{
    public Task HandleAsync(Guid learnerId, CancellationToken cancellationToken) =>
        db.TutorMessages.Where(message => message.LearnerId == learnerId).ExecuteDeleteAsync(cancellationToken);
}
