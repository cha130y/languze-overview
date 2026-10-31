using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Which version of a photo a file holds; the PostgreSQL enum <c>photo_kind</c> (FR-017).</summary>
public enum PhotoKind
{
    [PgName("PREPARED")]
    Prepared,

    [PgName("THUMBNAIL")]
    Thumbnail,
}
