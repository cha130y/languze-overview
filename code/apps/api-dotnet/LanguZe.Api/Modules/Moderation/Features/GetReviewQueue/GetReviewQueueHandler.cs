using System.Reflection;
using System.Text.Json.Serialization;
using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Moderation.Domain;
using LanguZe.Api.Modules.Moderation.Shared;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Features.GetReviewQueue;

/// <summary>
/// The cases waiting for an admin (review-queue.service.ts; FR-103, US-102, U4). Worked out
/// from the current state every time rather than kept as a list, so a case leaves when its pause
/// is lifted or given an end date, or the account is suspended or deleted, and a reactivated
/// account whose pause still awaits review comes back on its own.
/// </summary>
internal sealed class GetReviewQueueHandler(LanguZeDbContext db, TimeProvider clock)
{
    public async Task<ReviewQueueResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // A case awaiting review always has a pause neither lifted nor given an end date, so this
        // narrows the candidates to nearly the queue itself. Whether a learner belongs is decided
        // below by the rule their own AI access is decided by: they can hold an older pause awaiting
        // review and a newer one with an end date, and it is the newer one they are under.
        var candidates = await db.Users
            .Where(user => user.Status == AccountStatus.Active
                && db.AiSuspensions.Any(suspension => suspension.LearnerId == user.Id && suspension.LiftedAt == null && suspension.EndsAt == null))
            .Select(user => new
            {
                user.Id,
                Suspensions = db.AiSuspensions.Where(suspension => suspension.LearnerId == user.Id)
                    .Select(suspension => new { suspension.Reason, suspension.StartsAt, suspension.EndsAt, suspension.LiftedAt })
                    .ToList(),
                Blocks = db.PhotoBlocks.Where(block => block.LearnerId == user.Id).Select(block => block.Category).ToList(),
            })
            .ToListAsync(cancellationToken);

        var cases = new List<ReviewCaseResponse>();
        foreach (var learner in candidates)
        {
            var current = SuspensionRules.Current(learner.Suspensions, suspension => new SuspensionRecord(suspension.StartsAt, suspension.EndsAt, suspension.LiftedAt), now);
            if (current is null || current.EndsAt is not null)
            {
                continue;
            }

            cases.Add(new ReviewCaseResponse(learner.Id, AdminNames.Of(current.Reason), current.StartsAt, learner.Blocks.Count, Tally(learner.Blocks)));
        }

        // Oldest first, so cases are handled in the order they started waiting (FR-103).
        return new ReviewQueueResponse([.. cases.OrderBy(item => item.AwaitingSince)]);
    }

    /// <summary>
    /// Blocks grouped by category, the commonest first, then by name. The names are upper-case
    /// ASCII and underscores, where JavaScript's <c>localeCompare</c> and an ordinal comparison agree.
    /// </summary>
    private static IReadOnlyList<BlockTallyResponse> Tally(IEnumerable<BlockCategory> categories) =>
        [.. categories.GroupBy(category => AdminNames.Of(category))
            .Select(group => new BlockTallyResponse(group.Key, group.Count()))
            .OrderByDescending(tally => tally.Count)
            .ThenBy(tally => CategoryNames[tally.Category], StringComparer.Ordinal)];

    /// <summary>Each category by the name the response gives it.</summary>
    private static readonly Dictionary<BlockCategoryName, string> CategoryNames = Enum.GetValues<BlockCategoryName>()
        .ToDictionary(category => category, category => typeof(BlockCategoryName).GetField(category.ToString())!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name);
}
