using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>
/// Where the spot an occurrence's arrow points at came from (#73), so how often each fallback
/// is needed can be counted.
/// </summary>
public enum PointSource
{
    /// <summary>The model's point on the object, inside its box.</summary>
    [PgName("MODEL")]
    Model,

    /// <summary>No usable point, and the box holds no other word's box: its middle.</summary>
    [PgName("BOX_CENTRE")]
    BoxCentre,

    /// <summary>No usable point, and the box holds others: the part of it they leave free.</summary>
    [PgName("FREE_SPOT")]
    FreeSpot,
}
