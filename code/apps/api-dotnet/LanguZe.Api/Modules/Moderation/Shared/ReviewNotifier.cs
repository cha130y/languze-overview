using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Notifications.Contracts;
using LanguZe.Api.Platform.Config;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>
/// Emails the admins about a case that waits for them (review-notifier.service.ts; FR-107).
/// Only a pause that waits for a person is worth an email: a level 1 pause ends by itself after
/// seven days, and mail that needs no action teaches admins to ignore mail that does.
/// </summary>
internal sealed partial class ReviewNotifier(LanguZeDbContext db, IMailSender mail, ApiSettings settings, ILogger<ReviewNotifier> logger) : IReviewNotifier
{
    /// <summary>Where an admin goes to deal with it; the admin area serves this (FR-103).</summary>
    private const string ReviewQueuePath = "/admin/review-queue";

    public async Task CaseNeedsReviewAsync(RaisedSuspension suspension, CancellationToken cancellationToken)
    {
        if (suspension.EndsAt is not null)
        {
            return;
        }

        var to = await RecipientsAsync(cancellationToken);
        if (to.Count == 0)
        {
            // Loud on purpose. Admins are promoted by a deployment script that has not
            // necessarily run, so "nobody to tell" is a real state, and a case sitting in a
            // queue nobody has been told about is exactly what FR-107 prevents.
            LogNobodyToTell();
            return;
        }

        var queueUrl = $"{settings.WebOrigin.GetLeftPart(UriPartial.Authority)}{ReviewQueuePath}";
        await Task.WhenAll(to.Select(async address =>
        {
            try
            {
                await mail.SendAsync(ModerationEmails.CaseNeedsReview(address, queueUrl), cancellationToken);
            }
            catch (Exception error)
            {
                LogNotSent(error);
            }
        }));
    }

    /// <summary>Every active admin, or the configured address while there are none (FR-107).</summary>
    private async Task<IReadOnlyList<string>> RecipientsAsync(CancellationToken cancellationToken)
    {
        var admins = await db.Users
            .Where(user => user.Role == UserRole.Admin && user.Status == AccountStatus.Active)
            .Select(user => user.Email!)
            .ToListAsync(cancellationToken);

        if (admins.Count > 0)
        {
            return admins;
        }

        return settings.AdminAlertEmail.Length > 0 ? [settings.AdminAlertEmail] : [];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A case needs review but there is no admin to tell. Promote an admin, or set ADMIN_ALERT_EMAIL.")]
    private partial void LogNobodyToTell();

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not tell an admin that a case needs review")]
    private partial void LogNotSent(Exception error);
}
