using LanguZe.Api.Platform.OpenApi;
using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Users.Features.DeleteAccount;

/// <summary>NestJS's DeleteAccountDto, decorator for decorator.</summary>
/// <param name="Confirmation">Exactly "DELETE". Deletion is permanent, so it is never the result of one stray request (FR-007).</param>
public sealed record DeleteAccountRequest([property: Equals(DeleteAccountRequest.Word), ApiProperty(Enum = [DeleteAccountRequest.Word])] string Confirmation)
{
    /// <summary>The word a learner has to send to delete their account (FR-007, API design 3.2).</summary>
    public const string Word = "DELETE";
}
