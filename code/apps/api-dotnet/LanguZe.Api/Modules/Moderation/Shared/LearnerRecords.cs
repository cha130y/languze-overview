using System.Linq.Expressions;
using System.Text.Json.Serialization;
using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.OpenApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>
/// What an admin needs to judge a case, and nothing more (LearnerRecordDto; FR-104, US-103): no
/// name, no address, no photos, no worlds, no words, no tutor messages.
/// </summary>
/// <param name="CreatedAt">When the account was made; how old it is follows from this.</param>
/// <param name="Blocks">Every blocked photo, newest first.</param>
/// <param name="Suspensions">Every AI suspension, newest first, kept until the account goes (U8).</param>
internal sealed record LearnerRecordResponse(
    Guid LearnerId,
    AccountStatusName Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SignInMethodName> SignInMethods,
    IReadOnlyList<BlockRecordResponse> Blocks,
    IReadOnlyList<SuspensionHistoryResponse> Suspensions);

/// <summary>One blocked photo: what it was blocked for, and when. Never the photo (FR-095).</summary>
internal sealed record BlockRecordResponse(BlockCategoryName Category, DateTimeOffset CreatedAt);

/// <summary>One pause on AI features, past or present (FR-096, U8).</summary>
/// <param name="EndsAt">Empty means it waits for an admin to review it.</param>
/// <param name="LiftedAt">When an admin lifted it, if one did.</param>
internal sealed record SuspensionHistoryResponse(
    SuspensionReasonName Reason,
    DateTimeOffset StartsAt,
    [property: ApiPropertyOptional] DateTimeOffset? EndsAt,
    [property: ApiPropertyOptional] DateTimeOffset? LiftedAt);

/// <summary>
/// Reads a learner's moderation record (learner-record.service.ts; FR-104). The query names
/// every column the record holds and no others, so the photos, worlds, words, tutor messages,
/// name and address are never read: not filtered out afterwards, where a later change could
/// forget to, but never fetched at all.
/// </summary>
internal sealed class LearnerRecords(LanguZeDbContext db)
{
    /// <summary>The sign-in methods in the order the record lists them; a provider the admin area has no word for is left out.</summary>
    private static readonly (string Provider, SignInMethodName Method)[] Providers =
    [
        ("google", SignInMethodName.Google),
        ("line", SignInMethodName.Line),
        ("facebook", SignInMethodName.Facebook),
    ];

    public static AppException NoSuchLearner() => new(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "No such learner.");

    public async Task<LearnerRecordResponse> RecordWhereAsync(Expression<Func<User, bool>> which, CancellationToken cancellationToken)
    {
        var learner = await db.Users.Where(which)
            .Select(user => new
            {
                user.Id,
                user.Status,
                user.CreatedAt,
                // A password is "email" (FR-003); Better Auth's credential account became this column at cutover.
                HasPassword = user.PasswordHash != null,
                Providers = db.Set<IdentityUserLogin<Guid>>().Where(login => login.UserId == user.Id).Select(login => login.LoginProvider).ToList(),
                Blocks = db.PhotoBlocks.Where(block => block.LearnerId == user.Id)
                    .OrderByDescending(block => block.CreatedAt)
                    .Select(block => new { block.Category, block.CreatedAt })
                    .ToList(),
                Suspensions = db.AiSuspensions.Where(suspension => suspension.LearnerId == user.Id)
                    .OrderByDescending(suspension => suspension.StartsAt)
                    .Select(suspension => new { suspension.Reason, suspension.StartsAt, suspension.EndsAt, suspension.LiftedAt })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken) ?? throw NoSuchLearner();

        IReadOnlyList<SignInMethodName> methods =
        [
            .. learner.HasPassword ? [SignInMethodName.Email] : Array.Empty<SignInMethodName>(),
            .. Providers.Where(provider => learner.Providers.Contains(provider.Provider)).Select(provider => provider.Method),
        ];

        return new LearnerRecordResponse(
            learner.Id,
            AdminNames.Of(learner.Status),
            learner.CreatedAt,
            methods,
            [.. learner.Blocks.Select(block => new BlockRecordResponse(AdminNames.Of(block.Category), block.CreatedAt))],
            [.. learner.Suspensions.Select(suspension => new SuspensionHistoryResponse(AdminNames.Of(suspension.Reason), suspension.StartsAt, suspension.EndsAt, suspension.LiftedAt))]);
    }
}
