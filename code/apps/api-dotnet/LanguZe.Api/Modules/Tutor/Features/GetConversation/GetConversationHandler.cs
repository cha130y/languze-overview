using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Tutor.Features.GetConversation;

/// <summary>
/// The conversation, newest first (tutor.service.ts, <c>conversation</c>; FR-070). Older
/// messages come a page at a time, so a long conversation is not read in full to show the
/// last thing said, which is all a learner opening the page wants to see.
/// </summary>
internal sealed class GetConversationHandler(LanguZeDbContext db)
{
    /// <summary>How many messages one page carries.</summary>
    public const int MessagesPerPage = 30;

    public async Task<TutorConversationResponse> HandleAsync(Guid learnerId, Guid? before, CancellationToken cancellationToken)
    {
        IQueryable<TutorMessage> messages = db.TutorMessages.Where(message => message.LearnerId == learnerId);
        if (before is { } cursor)
        {
            // Prisma's cursor, as it writes it: everything up to the cursor's time, the first row
            // skipped. A cursor that is no message's has no time, so nothing comes before it.
            messages = messages
                .Where(message => message.CreatedAt <= db.TutorMessages.Where(seen => seen.Id == cursor).Select(seen => (DateTimeOffset?)seen.CreatedAt).FirstOrDefault())
                .OrderByDescending(message => message.CreatedAt)
                .Skip(1);
        }
        else
        {
            messages = messages.OrderByDescending(message => message.CreatedAt);
        }

        // One more is asked for than fits, so its presence answers "is there more?".
        var found = await messages.Take(MessagesPerPage + 1).ToListAsync(cancellationToken);
        var page = found.Take(MessagesPerPage)
            .Select(message => new TutorMessageResponse(message.Id, message.Role == TutorRole.Learner ? TutorRoleName.Learner : TutorRoleName.Tutor, message.Content, message.CreatedAt))
            .ToList();

        return new TutorConversationResponse(page, found.Count > MessagesPerPage ? page[^1].Id : null);
    }
}
