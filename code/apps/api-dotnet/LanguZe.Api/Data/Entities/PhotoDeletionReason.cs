using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Why a photo file is being removed; the PostgreSQL enum <c>photo_deletion_reason</c>.</summary>
public enum PhotoDeletionReason
{
    [PgName("WORLD_DELETED")]
    WorldDeleted,

    [PgName("ACCOUNT_DELETED")]
    AccountDeleted,

    [PgName("PHOTO_BLOCKED")]
    PhotoBlocked,

    [PgName("UPLOAD_FAILED")]
    UploadFailed,
}
