using Npgsql;

namespace LanguZe.Api.Contract.Support;

/// <summary>A blocked photo to seed: what it was blocked for, and when.</summary>
public sealed record SeededBlock(string Category, DateTimeOffset At);

/// <summary>A suspension of AI features to seed; no end means it waits for an admin.</summary>
public sealed record SeededSuspension(string Reason, DateTimeOffset StartsAt, DateTimeOffset? EndsAt = null, DateTimeOffset? LiftedAt = null);

/// <summary>
/// What only the database can set up, written with SQL into the database of the API under
/// test, as the NestJS end-to-end tests write it through Prisma: an admin, an account's
/// status, and a learner with blocked photos and AI suspensions. The API is never asked to
/// do it, and what is under test is always reached over HTTP. The tables and columns are
/// Prisma's, which both APIs share until cutover.
/// </summary>
public static class Database
{
    private static readonly Lazy<NpgsqlDataSource> Source = new(() => NpgsqlDataSource.Create(ConnectionString(
        Environment.GetEnvironmentVariable("CONTRACT_DATABASE_URL") is { Length: > 0 } url
            ? url
            : throw new InvalidOperationException("CONTRACT_DATABASE_URL is not set: run the tests with `pnpm test:contract`, which names the API's database."))));

    /// <summary>Gives the account the ADMIN role, as only the promote-admins tool can (FR-101), and answers its ID.</summary>
    public static async Task<Guid> PromoteToAdminAsync(string email)
    {
        await using var command = Source.Value.CreateCommand("UPDATE users SET role = 'ADMIN', updated_at = now() WHERE email = $1 RETURNING id");
        command.Parameters.AddWithValue(email.ToLowerInvariant());
        return (Guid)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? throw new InvalidOperationException($"No account {email}."));
    }

    public static Task SetRoleAsync(Guid accountId, string role) =>
        ExecuteAsync("UPDATE users SET role = $2::user_role, updated_at = now() WHERE id = $1", accountId, role);

    public static Task SetStatusAsync(Guid accountId, string status) =>
        ExecuteAsync("UPDATE users SET status = $2::account_status, updated_at = now() WHERE id = $1", accountId, status);

    /// <summary>
    /// A learner made straight in the database, with blocked photos and suspensions, as the
    /// admin e2e tests' <c>learnerWithCase</c> makes one. Answers their ID.
    /// </summary>
    public static async Task<Guid> LearnerAsync(string email, string status = "ACTIVE", IEnumerable<SeededBlock>? blocks = null, IEnumerable<SeededSuspension>? suspensions = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT INTO users (id, name, email, birth_year, terms_accepted_at, status, updated_at)
            VALUES ($1, 'ผู้เรียน', $2, $3, now(), $4::account_status, now())
            """,
            id,
            email.ToLowerInvariant(),
            (short)(DateTime.UtcNow.Year - 30),
            status);
        foreach (var block in blocks ?? [])
        {
            await BlockAsync(id, block);
        }

        foreach (var suspension in suspensions ?? [])
        {
            await SuspendAsync(id, suspension);
        }

        return id;
    }

    public static Task BlockAsync(Guid learnerId, SeededBlock block) =>
        ExecuteAsync(
            "INSERT INTO photo_blocks (id, learner_id, category, created_at) VALUES ($1, $2, $3::block_category, $4)",
            Guid.CreateVersion7(),
            learnerId,
            block.Category,
            block.At);

    public static Task SuspendAsync(Guid learnerId, SeededSuspension suspension) =>
        ExecuteAsync(
            """
            INSERT INTO ai_suspensions (id, learner_id, reason, starts_at, ends_at, lifted_at, updated_at)
            VALUES ($1, $2, $3::suspension_reason, $4, $5, $6, now())
            """,
            Guid.CreateVersion7(),
            learnerId,
            suspension.Reason,
            suspension.StartsAt,
            suspension.EndsAt is { } endsAt ? endsAt : DBNull.Value,
            suspension.LiftedAt is { } liftedAt ? liftedAt : DBNull.Value);

    /// <summary>When an account was made, as the database keeps it.</summary>
    public static async Task<DateTimeOffset> CreatedAtAsync(Guid accountId)
    {
        await using var command = Source.Value.CreateCommand("SELECT created_at FROM users WHERE id = $1");
        command.Parameters.AddWithValue(accountId);
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!, TimeSpan.Zero);
    }

    /// <summary>Now, to the millisecond, as a timestamptz(3) column keeps it and the API writes it.</summary>
    public static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>An instant as the API writes one.</summary>
    public static string Json(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task ExecuteAsync(string sql, params object[] values)
    {
        await using var command = Source.Value.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Npgsql's connection string from a <c>postgresql://</c> URL, as the runner and CI write the address.</summary>
    private static string ConnectionString(string url)
    {
        var uri = new Uri(url);
        var credentials = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
        };
        return builder.ConnectionString;
    }
}
