using System.Net;
using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>What happened, of the events the Computer Crime Act's traffic data covers (FR-110).</summary>
public enum TrafficEventKind
{
    [PgName("SIGN_UP")]
    SignUp,

    [PgName("SIGN_IN")]
    SignIn,

    [PgName("SIGN_IN_FAILED")]
    SignInFailed,

    [PgName("SIGN_OUT")]
    SignOut,

    [PgName("PHOTO_UPLOAD")]
    PhotoUpload,

    [PgName("TUTOR_MESSAGE")]
    TutorMessage,

    [PgName("ACCOUNT_DELETED")]
    AccountDeleted,
}

/// <summary>
/// One event of the traffic data kept 90 days (data model, <c>traffic_events</c>; FR-110,
/// ADR-0007): who, from which address, and when. Never what they typed.
/// </summary>
public sealed class TrafficEvent
{
    public Guid Id { get; set; }

    public TrafficEventKind Kind { get; set; }

    /// <summary>Empty for a failed sign-in to an address no account has.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>How a sign-up or sign-in was made: <c>password</c>, <c>google</c>, <c>line</c> or <c>facebook</c>.</summary>
    public string? Via { get; set; }

    public IPAddress? Ip { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
