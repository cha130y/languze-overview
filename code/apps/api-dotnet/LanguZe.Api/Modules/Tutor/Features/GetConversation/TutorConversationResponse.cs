using System.Text.Json.Serialization;
using LanguZe.Api.Platform.OpenApi;

namespace LanguZe.Api.Modules.Tutor.Features.GetConversation;

/// <summary>A page of the learner's one conversation, newest first (TutorConversationDto; FR-070, V6).</summary>
/// <param name="NextCursor">Pass as <c>before</c> for older messages; empty at the beginning.</param>
internal sealed record TutorConversationResponse(IReadOnlyList<TutorMessageResponse> Messages, [property: ApiPropertyOptional] Guid? NextCursor);

/// <summary>One thing said, by the learner or by the tutor (TutorMessageDto).</summary>
internal sealed record TutorMessageResponse(Guid Id, TutorRoleName Role, string Content, DateTimeOffset CreatedAt);

internal enum TutorRoleName
{
    [JsonStringEnumMemberName("LEARNER")]
    Learner,

    [JsonStringEnumMemberName("TUTOR")]
    Tutor,
}
