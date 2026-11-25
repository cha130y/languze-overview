using System.Text.Json;
using LanguZe.Api.Modules.Auth.Domain;

namespace LanguZe.Api.Tests.Auth;

/// <summary>
/// The checks Better Auth makes past the request's validation give the answers it gives:
/// addresses as the zod it uses judges them, read from validator-vectors.json, and
/// passwords measured in UTF-16 units.
/// </summary>
public sealed class BetterAuthRulesTests
{
    private static readonly JsonElement Vectors = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Platform", "Validation", "validator-vectors.json"))).RootElement;

    private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);

    // The pattern is zod's of this version; a newer one in apps/api needs reading again.
    [Fact]
    public void The_vectors_come_from_the_zod_version_better_auth_uses() =>
        Assert.Equal("4.6.7", Vectors.GetProperty("zod").GetString());

    [Fact]
    public void An_email_address_is_accepted_exactly_when_zod_accepts_it()
    {
        var mismatches = Vectors.GetProperty("zodEmail").EnumerateArray()
            .Select(vector => (Address: vector[0].GetString()!, Accepted: vector[1].GetBoolean()))
            .Where(vector => BetterAuthRules.IsEmail(vector.Address) != vector.Accepted)
            .Select(vector => $"{JsonSerializer.Serialize(vector.Address)} should be {(vector.Accepted ? "accepted" : "refused")}")
            .ToList();

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void A_password_is_8_to_128_characters_long(int length, bool accepted) =>
        Assert.Equal(accepted, BetterAuthRules.IsPasswordLength(new string('a', length)));

    // validator.js counts an emoji once, so the request's own rule lets through what Better Auth refuses.
    [Theory]
    [InlineData(4, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void An_emoji_counts_as_two_characters_of_a_password(int emoji, bool accepted) =>
        Assert.Equal(accepted, BetterAuthRules.IsPasswordLength(string.Concat(Enumerable.Repeat(Emoji, emoji))));
}
