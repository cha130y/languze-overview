namespace LanguZe.Api.Data.Entities;

/// <summary>
/// What identifies an account after it is deleted, kept 90 days because the Computer Crime
/// Act requires it (data model, <c>closed_accounts</c>; FR-111, ADR-0007). Everything else
/// the learner had went with the account (FR-007).
/// </summary>
public sealed class ClosedAccount
{
    public Guid AccountId { get; set; }

    /// <summary>Empty when the account had none, or only a placeholder (D1).</summary>
    public string? Email { get; set; }

    /// <summary>Each linked sign-in as <c>provider:key</c>, such as <c>line:U1234…</c>.</summary>
    public List<string> ProviderAccounts { get; set; } = [];

    public DateTimeOffset AccountCreatedAt { get; set; }

    public DateTimeOffset ClosedAt { get; set; }

    /// <summary>Whether an admin deleted it (FR-105), rather than the learner (FR-007).</summary>
    public bool ClosedByAdmin { get; set; }
}
