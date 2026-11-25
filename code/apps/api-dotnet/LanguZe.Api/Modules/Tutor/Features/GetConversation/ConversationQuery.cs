using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Tutor.Features.GetConversation;

/// <summary>Reading older messages (ConversationQuery).</summary>
/// <param name="Before">The oldest message already seen; older ones follow it.</param>
internal sealed record ConversationQuery([property: IsOptional, IsUuid] string? Before);
