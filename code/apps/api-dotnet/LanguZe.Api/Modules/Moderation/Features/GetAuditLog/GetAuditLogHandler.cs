using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Moderation.Shared;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Moderation.Features.GetAuditLog;

/// <summary>Every admin action, newest first, a page at a time (admin-actions.service.ts, <c>log</c>; FR-106, US-105).</summary>
internal sealed class GetAuditLogHandler(LanguZeDbContext db)
{
    /// <summary>How many audit entries one page holds.</summary>
    public const int EntriesPerPage = 50;

    public async Task<AuditLogResponse> HandleAsync(Guid? before, CancellationToken cancellationToken)
    {
        // Prisma's cursor over two columns, as it writes it: everything up to the cursor's entry
        // in the same order, the first row skipped. A cursor that is no entry's matches nothing.
        var entries = before is { } cursor
            ? db.AdminActions.FromSql($"""
                SELECT * FROM admin_actions
                WHERE (created_at = (SELECT created_at FROM admin_actions WHERE id = {cursor})
                       AND id <= (SELECT id FROM admin_actions WHERE id = {cursor}))
                   OR created_at < (SELECT created_at FROM admin_actions WHERE id = {cursor})
                """)
            : db.AdminActions;

        // The ID breaks ties, so entries made in the same millisecond keep one order.
        var rows = await entries.OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id)
            .Skip(before is null ? 0 : 1)
            .Take(EntriesPerPage + 1)
            .ToListAsync(cancellationToken);

        // One more was asked for than fits, so its presence answers "is there more?".
        var page = rows.Take(EntriesPerPage).Select(AuditEntries.ResponseOf).ToList();
        return new AuditLogResponse(page, rows.Count > EntriesPerPage ? page[^1].Id : null);
    }
}
