using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Moderation.Contracts;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>Answers auth's question for <c>/v1/me</c> with the decision the AI endpoints enforce (rule R6).</summary>
internal sealed class AccountAiAccessReader(IAiAccess aiAccess) : IAccountAiAccess
{
    public async Task<AccountAiAccess> ForAsync(Guid accountId, bool verifiedForAi, CancellationToken cancellationToken)
    {
        var access = await aiAccess.ForAsync(accountId, verifiedForAi, cancellationToken);
        return new AccountAiAccess(
            access.Available,
            access.Reason switch
            {
                AiAccessReason.NotVerified => AccountAiAccessReason.NotVerified,
                AiAccessReason.AiSuspended => AccountAiAccessReason.AiSuspended,
                _ => null,
            },
            access.Until,
            access.AwaitingReview);
    }
}
