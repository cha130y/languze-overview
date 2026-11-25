using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Tutor.Features.SendMessage;

/// <summary>A message to the tutor (SendMessageDto; FR-070). Its length rule is V7, checked by the handler as a rule of its own.</summary>
/// <param name="Content">What the learner is asking, 1 to 1,000 characters.</param>
internal sealed record SendMessageRequest(
    [property: IsString, IsNotEmpty, ApiProperty(MaxLength = SendMessageHandler.MaxMessageLength)] string Content);
