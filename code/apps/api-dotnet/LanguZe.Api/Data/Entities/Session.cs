namespace LanguZe.Api.Data.Entities;

/// <summary>
/// A signed-in browser: the authentication ticket its cookie points at, kept in the
/// database so that signing out, a password reset or a suspension ends it at once
/// (ADR-0006, section 5; FR-005, FR-105, NFR-019). Deleted with its user.
/// </summary>
public sealed class Session
{
    /// <summary>UUID version 7 (D5); the only thing the cookie carries besides its signature.</summary>
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The serialized authentication ticket.</summary>
    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
}
