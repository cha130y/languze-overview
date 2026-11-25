using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguZe.Api.Modules.Moderation.Features.GetAuditLog;

/// <summary>Every admin action, newest first (FR-106, US-105).</summary>
internal static class GetAuditLogEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/admin/audit-log", HandleAsync)
            .WithMetadata(new QueryMetadata(typeof(AuditLogQuery)))
            .AdminOnly();

    private static async Task<Ok<AuditLogResponse>> HandleAsync(HttpContext context, GetAuditLogHandler handler, CancellationToken cancellationToken)
    {
        var query = RequestQuery.Check<AuditLogQuery>(context);
        return TypedResults.Ok(await handler.HandleAsync(query.Before is null ? null : Guid.Parse(query.Before), cancellationToken));
    }
}
