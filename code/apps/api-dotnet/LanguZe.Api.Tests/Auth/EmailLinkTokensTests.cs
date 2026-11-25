using LanguZe.Api.Modules.Auth.Shared;
using LanguZe.Api.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;

namespace LanguZe.Api.Tests.Auth;

/// <summary>The tokens in account emails' links: an hour long, for one purpose, and nothing anyone could make up.</summary>
public sealed class EmailLinkTokensTests
{
    private const string Purpose = "LanguZe.Tests.Link";

    private readonly ManualClock _clock = new();
    private readonly EmailLinkTokens _tokens;

    public EmailLinkTokensTests() =>
        _tokens = new EmailLinkTokens(new EphemeralDataProtectionProvider(), _clock);

    [Fact]
    public void A_token_gives_back_its_payload()
    {
        var token = _tokens.Create(Purpose, "learner@example.com");

        Assert.Equal("learner@example.com", _tokens.Read(Purpose, token));
    }

    [Fact]
    public void A_token_works_until_an_hour_has_passed()
    {
        var token = _tokens.Create(Purpose, "learner@example.com");

        _clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(1));
        Assert.NotNull(_tokens.Read(Purpose, token));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(_tokens.Read(Purpose, token));
    }

    [Fact]
    public void A_token_made_for_one_purpose_opens_no_other() =>
        Assert.Null(_tokens.Read("LanguZe.Tests.Other", _tokens.Create(Purpose, "learner@example.com")));

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("not a token at all!")]
    [InlineData("")]
    public void A_made_up_token_reads_as_none(string token) =>
        Assert.Null(_tokens.Read(Purpose, token));

    [Fact]
    public void A_changed_token_reads_as_none()
    {
        var token = _tokens.Create(Purpose, "learner@example.com");
        var changed = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];

        Assert.Null(_tokens.Read(Purpose, changed));
    }

    // VerifyEmailDto takes a token of 512 characters at most.
    [Fact]
    public void A_token_for_the_longest_address_fits_the_request()
    {
        var longest = new string('a', 64) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 61), 3)) + ".com";

        Assert.Equal(254, longest.Length);
        Assert.InRange(_tokens.Create(Purpose, longest).Length, 1, 512);
    }
}
