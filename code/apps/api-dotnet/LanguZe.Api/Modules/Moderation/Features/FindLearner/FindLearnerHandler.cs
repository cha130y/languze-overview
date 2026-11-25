using LanguZe.Api.Modules.Moderation.Shared;
using LanguZe.Api.Platform.Email;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Text;

namespace LanguZe.Api.Modules.Moderation.Features.FindLearner;

/// <summary>
/// One learner, by an exact address or an account ID (learner-record.service.ts, <c>find</c>;
/// FR-108, US-106). There is no partial match and no list: an admin who does not already hold
/// the address or the ID cannot browse their way to someone.
/// </summary>
internal sealed class FindLearnerHandler(LearnerRecords records)
{
    private const string ExactlyOne = "Give exactly one of email or accountId.";

    public Task<LearnerRecordResponse> HandleAsync(FindLearnerQuery query, CancellationToken cancellationToken)
    {
        if (query.Email is not null && query.AccountId is not null)
        {
            throw NotExactlyOne();
        }

        if (query.AccountId is not null)
        {
            var id = Guid.Parse(query.AccountId);
            return records.RecordWhereAsync(user => user.Id == id, cancellationToken);
        }

        if (query.Email is null)
        {
            throw NotExactlyOne();
        }

        // Lower-cased and matched exactly: every address the application stores is lower-cased on
        // the way in, so this finds anyone who signed up, and can never find two of them.
        var address = JsText.Trim(query.Email).ToLowerInvariant();

        // A placeholder is built from a provider's own user ID; searching by one would only be a
        // way of probing those IDs. Such learners are found by account ID instead (D1).
        if (PlaceholderAddress.Is(address))
        {
            throw LearnerRecords.NoSuchLearner();
        }

        return records.RecordWhereAsync(user => user.Email == address, cancellationToken);
    }

    /// <summary>The service's own refusal of a field, so <c>details.fields</c> is an object of field to message.</summary>
    private static AppException NotExactlyOne() =>
        new(
            ErrorCodes.ValidationFailed,
            StatusCodes.Status400BadRequest,
            ExactlyOne,
            new Dictionary<string, object?> { ["fields"] = new Dictionary<string, string> { ["email"] = ExactlyOne } });
}
