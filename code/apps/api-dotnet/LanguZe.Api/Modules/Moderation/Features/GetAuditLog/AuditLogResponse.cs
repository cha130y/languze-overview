using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.OpenApi;

namespace LanguZe.Api.Modules.Moderation.Features.GetAuditLog;

/// <summary>A page of the audit log, newest first (AuditLogDto; FR-106, US-105).</summary>
/// <param name="NextCursor">Pass as <c>before</c> for older entries; empty at the beginning.</param>
internal sealed record AuditLogResponse(IReadOnlyList<AuditEntryResponse> Entries, [property: ApiPropertyOptional] Guid? NextCursor);
