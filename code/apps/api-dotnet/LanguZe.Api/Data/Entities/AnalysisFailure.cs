using NpgsqlTypes;

namespace LanguZe.Api.Data.Entities;

/// <summary>Why an analysis ended without words (FR-025); the PostgreSQL enum <c>analysis_failure</c>.</summary>
public enum AnalysisFailure
{
    [PgName("BLOCKED")]
    Blocked,

    [PgName("TOO_FEW_WORDS")]
    TooFewWords,

    [PgName("PROVIDER_ERROR")]
    ProviderError,

    [PgName("INVALID_OUTPUT")]
    InvalidOutput,

    [PgName("TIMED_OUT")]
    TimedOut,
}
