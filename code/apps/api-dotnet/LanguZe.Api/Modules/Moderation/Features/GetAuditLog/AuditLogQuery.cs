using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Moderation.Features.GetAuditLog;

/// <summary>Reading further back in the audit log (AuditLogQuery).</summary>
/// <param name="Before">The oldest entry already seen; older ones follow it.</param>
internal sealed record AuditLogQuery([property: IsOptional, IsUuid] string? Before);
