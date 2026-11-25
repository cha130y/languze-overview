using LanguZe.Api.Data.Entities;
using LanguZe.Api.Data;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Users.Contracts;
using LanguZe.Api.Platform.Records;

namespace LanguZe.Api.Modules.Users.Features.DeleteAccount;

/// <summary>
/// Deletes the signed-in learner's account (FR-007, US-009), in auth.service.ts's order: the
/// browser is signed out first and the account goes last, in a transaction of its own, so
/// that if anything fails the account survives rather than being half removed.
/// </summary>
internal sealed class DeleteAccountHandler(LanguZeDbContext db, IPhotoCleanup photoCleanup, IClosedAccountExpiry closedAccounts, TrafficLog traffic, TimeProvider clock)
{
    public async Task HandleAsync(Guid accountId, HttpContext context, CancellationToken cancellationToken)
    {
        await BrowserSession.SignOutAsync(context);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        await AccountDeletion.DeleteAsync(db, accountId, byAdmin: false, now, cancellationToken);
        await traffic.RecordAsync(TrafficEventKind.AccountDeleted, accountId, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Only now are the cleanup records there for the task to find.
        photoCleanup.Expect();
        closedAccounts.Expect(now);
    }
}
