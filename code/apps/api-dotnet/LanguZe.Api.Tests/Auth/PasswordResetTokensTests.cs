using LanguZe.Api.Modules.Auth.Shared;
using LanguZe.Api.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;

namespace LanguZe.Api.Tests.Auth;

/// <summary>A reset link's token names the account and its security stamp, and no other kind of token passes for one.</summary>
public sealed class PasswordResetTokensTests
{
    private static readonly Guid Learner = Guid.Parse("01900000-0000-7000-8000-000000000001");

    private readonly EmailLinkTokens _links = new(new EphemeralDataProtectionProvider(), new ManualClock());
    private readonly PasswordResetTokens _tokens;

    public PasswordResetTokensTests() => _tokens = new PasswordResetTokens(_links);

    [Fact]
    public void A_token_gives_back_the_account_and_its_stamp() =>
        Assert.Equal((Learner, "A-STAMP"), _tokens.Read(_tokens.Create(Learner, "A-STAMP")));

    // A stamp could hold the separator; only the first one splits.
    [Fact]
    public void A_stamp_is_given_back_whole() =>
        Assert.Equal("A|STAMP", _tokens.Read(_tokens.Create(Learner, "A|STAMP"))?.SecurityStamp);

    [Fact]
    public void A_verification_token_does_not_pass_for_a_reset_token() =>
        Assert.Null(_tokens.Read(_links.Create(VerificationEmails.Purpose, $"{Learner:N}|A-STAMP")));

    [Fact]
    public void A_made_up_token_reads_as_none() =>
        Assert.Null(_tokens.Read("not-a-token"));
}
