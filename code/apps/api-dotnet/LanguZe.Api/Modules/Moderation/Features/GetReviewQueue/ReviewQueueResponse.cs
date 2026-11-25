using LanguZe.Api.Modules.Moderation.Shared;

namespace LanguZe.Api.Modules.Moderation.Features.GetReviewQueue;

/// <summary>
/// Every case awaiting review, oldest first (ReviewQueueDto; FR-103). Not paged: the queue is a
/// work list that admins are meant to empty, and one long enough to need paging is itself the
/// thing to deal with.
/// </summary>
internal sealed record ReviewQueueResponse(IReadOnlyList<ReviewCaseResponse> Cases);

/// <summary>One case waiting for an admin (ReviewQueueItemDto; FR-103, US-102).</summary>
/// <param name="LearnerId">Who the case is about. The queue carries no other detail about them; the moderation record does, behind its own request (FR-104).</param>
/// <param name="AwaitingSince">When the pause began. The queue is ordered by it, oldest first.</param>
/// <param name="BlockCount">Every blocked photo on the learner's record.</param>
/// <param name="Blocks">The same blocks broken down by category, most of them first.</param>
internal sealed record ReviewCaseResponse(Guid LearnerId, SuspensionReasonName Reason, DateTimeOffset AwaitingSince, int BlockCount, IReadOnlyList<BlockTallyResponse> Blocks);

/// <summary>How many of a learner's photos were blocked for one reason (BlockTallyDto; FR-095, FR-103).</summary>
internal sealed record BlockTallyResponse(BlockCategoryName Category, int Count);
