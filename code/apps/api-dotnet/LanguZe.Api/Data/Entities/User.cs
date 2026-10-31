using Microsoft.AspNetCore.Identity;

namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A learner or an admin (data model, section 4.1): ASP.NET Core Identity's user on the
/// existing <c>users</c> table, keeping each learner's ID so every foreign key in the
/// domain stays (ADR-0006, section 5). The email address is also Identity's user name.
/// Identity's lockout, phone and two-factor features are left out: LanguZe has none of
/// them, and rate limits stand where a lockout would.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    /// <summary>Display name, 1–50 characters (V12, V19).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Left empty: LanguZe shows no profile pictures.</summary>
    public string? Image { get; set; }

    public UserRole Role { get; set; }

    public AccountStatus Status { get; set; }

    /// <summary>Empty only for a provider sign-up that has not accepted the Terms yet (FR-090, U5).</summary>
    public DateTimeOffset? TermsAcceptedAt { get; set; }

    /// <summary>Given at sign-up; learners must turn 18 this year (FR-090, V20).</summary>
    public short? BirthYear { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
