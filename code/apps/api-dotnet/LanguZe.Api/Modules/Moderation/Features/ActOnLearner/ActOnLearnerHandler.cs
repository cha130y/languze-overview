using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Moderation.Domain;
using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Users.Contracts;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Json;
using LanguZe.Api.Platform.Records;
using LanguZe.Api.Platform.Text;
using LanguZe.Api.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Features.ActOnLearner;

/// <summary>
/// What an admin can do to a learner's case, and the record that they did it
/// (admin-actions.service.ts; FR-105, FR-106; moderation flow section 3). Every action and its
/// audit entry are written in one transaction, so there is never a change without an entry,
/// nor an entry for a change that did not happen; the table refuses to have an entry changed.
/// </summary>
internal sealed class ActOnLearnerHandler(LanguZeDbContext db, IPhotoCleanup photoCleanup, IClosedAccountExpiry closedAccounts, TrafficLog traffic, TimeProvider clock)
{
    /// <summary>The longest reason an admin can give (FR-105, V19).</summary>
    public const int MaxReasonLength = 500;

    public async Task<AuditEntryResponse> HandleAsync(Guid adminId, Guid learnerId, ActRequest request, CancellationToken cancellationToken)
    {
        // Checked before anything else is looked at: an admin's own case is not theirs to judge,
        // whatever it is, and no answer about the account should leak out on the way to saying so.
        if (adminId == learnerId)
        {
            throw new AppException(ErrorCodes.OwnAccount, StatusCodes.Status403Forbidden, "An admin cannot act on their own account.");
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        var action = Enum.Parse<AdminActionType>(Enum.Parse<AdminActionName>(Pascal(request.Action)).ToString());
        var endsAt = EndsAtFor(action, request.EndsAt, now);
        var reason = JsText.Trim(request.Reason);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The learner's row is locked for the length of the action. Whether an action fits is
        // decided from what this reads, and two admins acting on the same case at once would
        // otherwise both pass the check, leaving the audit log claiming two changes for one.
        var status = (await db.Database.SqlQuery<string>($"SELECT status::text AS \"Value\" FROM users WHERE id = {learnerId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw LearnerRecords.NoSuchLearner();

        var details = await ApplyAsync(action, adminId, learnerId, status == "SUSPENDED", endsAt, now, cancellationToken);

        var entry = new AdminAction
        {
            AdminId = adminId,
            LearnerId = learnerId,
            Action = action,
            Reason = reason,
            Details = details is null ? null : AuditEntries.DetailsOf(details),
            // The same instant the change records, so the two always agree.
            CreatedAt = now,
        };
        db.AdminActions.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (action == AdminActionType.DeleteAccount)
        {
            photoCleanup.Expect();
            closedAccounts.Expect(now);
        }

        return AuditEntries.ResponseOf(entry);
    }

    /// <summary>
    /// The end date an action carries: required when setting one, refused otherwise. An end date
    /// sent with any other action is a client that has lost track of what it is asking for. An
    /// empty one is none: a form that always sends the field is not wrong.
    /// </summary>
    private static DateTimeOffset? EndsAtFor(AdminActionType action, string? given, DateTimeOffset now)
    {
        if (action != AdminActionType.SetAiSuspensionEnd)
        {
            return given is null ? null : throw Invalid("endsAt", "An end date belongs only to SET_AI_SUSPENSION_END.");
        }

        if (given is null)
        {
            throw Invalid("endsAt", "An end date is required.");
        }

        // The rules let through only what JavaScript's Date reads, so this always reads it.
        JsDate.TryParse(given, TimeZoneInfo.Local, out var endsAt);
        return endsAt <= now ? throw Invalid("endsAt", "The end date must be in the future.") : endsAt;
    }

    private async Task<AuditDetailsResponse?> ApplyAsync(AdminActionType action, Guid adminId, Guid learnerId, bool suspended, DateTimeOffset? endsAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case AdminActionType.LiftAiSuspension:
                {
                    var inForce = await SuspensionsInForceAsync(learnerId, now, cancellationToken);
                    if (inForce.Count == 0)
                    {
                        throw NotApplicable("There is no AI suspension in force to lift.");
                    }

                    // Every suspension in force, not only the latest: after a lift the learner can use
                    // AI features again at once (US-104), which an older one left in force would stop.
                    var ids = inForce.Select(suspension => suspension.Id).ToList();
                    await db.AiSuspensions.Where(suspension => ids.Contains(suspension.Id))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(suspension => suspension.LiftedAt, now).SetProperty(suspension => suspension.LiftedBy, adminId).SetProperty(suspension => suspension.UpdatedAt, now), cancellationToken);
                    return null;
                }

            case AdminActionType.SetAiSuspensionEnd:
                {
                    var inForce = await SuspensionsInForceAsync(learnerId, now, cancellationToken);
                    var current = SuspensionRules.Current(inForce, suspension => new SuspensionRecord(suspension.StartsAt, suspension.EndsAt, suspension.LiftedAt), now);
                    if (current is null || endsAt is not { } newEnd)
                    {
                        throw NotApplicable("There is no AI suspension in force to end.");
                    }

                    // Every one in force, for the same reason as a lift: the date is the date.
                    var ids = inForce.Select(suspension => suspension.Id).ToList();
                    await db.AiSuspensions.Where(suspension => ids.Contains(suspension.Id))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(suspension => suspension.EndsAt, newEnd).SetProperty(suspension => suspension.UpdatedAt, now), cancellationToken);
                    return new AuditDetailsResponse(UtcInstantConverter.ToJson(newEnd), current.EndsAt is { } previous ? UtcInstantConverter.ToJson(previous) : null);
                }

            case AdminActionType.SuspendAccount:
                {
                    if (suspended)
                    {
                        throw NotApplicable("The account is already suspended.");
                    }

                    await db.Users.Where(user => user.Id == learnerId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Status, AccountStatus.Suspended).SetProperty(user => user.UpdatedAt, now), cancellationToken);

                    // Signed out everywhere, immediately (FR-105, US-104 criterion 4). The session guard
                    // refuses a suspended account anyway; this makes the refusal arrive as being signed
                    // out rather than as an error on a page that still seems signed in.
                    await db.Sessions.Where(session => session.UserId == learnerId).ExecuteDeleteAsync(cancellationToken);
                    return null;
                }

            case AdminActionType.ReactivateAccount:
                {
                    if (!suspended)
                    {
                        throw NotApplicable("The account is not suspended.");
                    }

                    await db.Users.Where(user => user.Id == learnerId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Status, AccountStatus.Active).SetProperty(user => user.UpdatedAt, now), cancellationToken);
                    return null;
                }

            default:
                // The same deletion as the learner's own (FR-007, US-104 criterion 5).
                await AccountDeletion.DeleteAsync(db, learnerId, byAdmin: true, now, cancellationToken);
                // Saved with the audit entry; the admin who acted is named there (FR-106).
                traffic.Add(TrafficEventKind.AccountDeleted, learnerId);
                return null;
        }
    }

    private Task<List<AiSuspension>> SuspensionsInForceAsync(Guid learnerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.AiSuspensions.Where(suspension => suspension.LearnerId == learnerId && suspension.LiftedAt == null && (suspension.EndsAt == null || suspension.EndsAt > now))
            .ToListAsync(cancellationToken);

    /// <summary>The service's own refusal of a field, so <c>details.fields</c> is an object of field to message.</summary>
    private static AppException Invalid(string field, string message) =>
        new(ErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest, message, new Dictionary<string, object?> { ["fields"] = new Dictionary<string, string> { [field] = message } });

    private static AppException NotApplicable(string message) => new(ErrorCodes.ActionNotApplicable, StatusCodes.Status409Conflict, message);

    /// <summary><c>SET_AI_SUSPENSION_END</c> as the enum's member name.</summary>
    private static string Pascal(string name) => string.Concat(name.Split('_').Select(word => word[..1] + word[1..].ToLowerInvariant()));
}
