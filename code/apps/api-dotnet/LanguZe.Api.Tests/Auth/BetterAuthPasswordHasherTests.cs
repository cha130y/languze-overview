using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Auth.Shared;
using Microsoft.AspNetCore.Identity;

namespace LanguZe.Api.Tests.Auth;

/// <summary>
/// Passwords Better Auth hashed keep working after the cutover. The hashes are real ones,
/// written by Better Auth 1.7.5 in apps/api:
/// <c>node --input-type=module -e "import { hashPassword } from 'better-auth/crypto'; console.log(await hashPassword('…'))"</c>
/// </summary>
public sealed class BetterAuthPasswordHasherTests
{
    /// <summary>Better Auth's hash of <c>correct-horse-42</c>.</summary>
    internal const string Hash =
        "97b458cd1f3500aac76fab247393f88f:cb4bf02b37e4f822783d8ee54848ffacad6a90874fa1346cda3a29aa584d15e509b151bb219c8f77becad61197b747355422ea478e8d9bd72df2468c4f1f974f";

    internal const string Password = "correct-horse-42";

    /// <summary>Better Auth's hash of <c>ｐａｓｓｗｏｒｄ-ทดสอบ</c>, in full-width letters, which NFKC reads as <c>password-ทดสอบ</c>.</summary>
    private const string FullWidthHash =
        "64290b50ced65e8486409a8a80db2a00:892d7a8ce7c9d21d17b3e1b8aec7eefca9fdfde97d544c794ee3f2f4b60d4edab5495e2c9eddb940eff5f4065066222fa46d6466e29d71206f135946d4ba1b63";

    private readonly BetterAuthPasswordHasher _hasher = new();
    private readonly User _user = new();

    [Fact]
    public void The_right_password_matches_a_better_auth_hash_and_asks_for_a_rehash() =>
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.VerifyHashedPassword(_user, Hash, Password));

    [Fact]
    public void A_wrong_password_does_not_match_a_better_auth_hash() =>
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(_user, Hash, "correct-horse-43"));

    [Theory]
    [InlineData("ｐａｓｓｗｏｒｄ-ทดสอบ")]
    [InlineData("password-ทดสอบ")]
    public void A_password_is_compared_in_its_nfkc_form_as_better_auth_compares_it(string password) =>
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.VerifyHashedPassword(_user, FullWidthHash, password));

    [Fact]
    public void A_new_password_is_hashed_in_identitys_format_and_needs_no_rehash()
    {
        var hash = _hasher.HashPassword(_user, Password);

        Assert.DoesNotContain(":", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(_user, hash, Password));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(_user, hash, "correct-horse-43"));
    }
}
