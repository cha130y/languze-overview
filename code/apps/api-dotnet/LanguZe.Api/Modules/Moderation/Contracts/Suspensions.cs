using LanguZe.Api.Data.Entities;

namespace LanguZe.Api.Modules.Moderation.Contracts;

/// <summary>A pause a blocked photo has just raised; what telling the admins needs.</summary>
/// <param name="EndsAt">When it lifts itself; empty while it waits for an admin (FR-103).</param>
public sealed record RaisedSuspension(Guid Id, DateTimeOffset? EndsAt);

/// <summary>Raises the pause a blocked photo has earned (suspension.service.ts, <c>raiseFor</c>), for other modules (rule R3).</summary>
public interface ISuspensions
{
    /// <summary>
    /// Raises the pause the learner's latest block has earned, if any (V15, FR-098). It works
    /// on the caller's database context, inside the transaction that recorded the block, so
    /// the count it reads already includes that photo and no two blocks landing together can
    /// each decide they were the second (moderation flow, section 1). Answers the suspension
    /// raised, so the caller can tell the admins once the transaction has committed.
    /// </summary>
    Task<RaisedSuspension?> RaiseForAsync(Guid learnerId, BlockCategory category, CancellationToken cancellationToken);
}

/// <summary>Tells admins a case needs them (review-notifier.service.ts; FR-107), for other modules (rule R3).</summary>
public interface IReviewNotifier
{
    /// <summary>
    /// Called once the transaction that raised the pause has committed, never inside it. A
    /// failure to send is logged rather than thrown: the pause is already in force, the queue
    /// is the record, and the email only hurries someone along.
    /// </summary>
    Task CaseNeedsReviewAsync(RaisedSuspension suspension, CancellationToken cancellationToken);
}
