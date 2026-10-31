using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LanguZe.Api.Contract.Support;

public sealed record Learner(string Name, string Email, string Password)
{
    public static Learner New(string tag) => new(
        $"Contract {tag}",
        $"contract-{tag}-{Guid.NewGuid():N}@example.com",
        "correct-horse-42");

    public object SignUpBody(int? birthYear = null, bool acceptTerms = true) => new
    {
        email = Email,
        password = Password,
        name = Name,
        birthYear = birthYear ?? DateTime.UtcNow.Year - 30,
        acceptTerms,
    };

    public object SignInBody(string? password = null) => new { email = Email, password = password ?? Password };
}

public static class Learners
{
    /// <summary>Signs a new learner up; the client then holds their session.</summary>
    public static async Task<Learner> SignUpAsync(this ApiClient client, string tag)
    {
        var learner = Learner.New(tag);
        var response = await client.PostAsync("/v1/auth/sign-up", learner.SignUpBody());
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return learner;
    }

    /// <summary>Signs a new learner up and verifies their address with the emailed link, as the AI features need (FR-006).</summary>
    public static async Task<Learner> SignUpVerifiedAsync(this ApiClient client, string tag)
    {
        var learner = await client.SignUpAsync(tag);
        var token = await Mailbox.TokenFromEmailAsync(learner.Email, "/verify-email");
        var response = await client.PostAsync("/v1/auth/verify-email", new { token });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return learner;
    }
}

/// <summary>An email as Maildev received it.</summary>
public sealed record Email(string Subject, string Text, string Html);

/// <summary>
/// Reads the mail the API sent, from Maildev's REST API. The API under test sends to
/// Maildev because no Resend key is set.
/// </summary>
public static class Mailbox
{
    private static readonly Uri MaildevUrl = new(
        Environment.GetEnvironmentVariable("MAILDEV_URL")
        ?? $"http://localhost:{Environment.GetEnvironmentVariable("MAILDEV_WEB_PORT") ?? "1081"}");

    private static readonly HttpClient Http = new() { BaseAddress = MaildevUrl };

    /// <summary>Waits for the latest email to <paramref name="to"/> linking to <paramref name="route"/> and returns its token.</summary>
    public static async Task<string> TokenFromEmailAsync(string to, string route)
    {
        var link = new Regex($@"{Regex.Escape(route)}\?token=([^\s""'<>&]+)");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var emails = await Http.GetFromJsonAsync<JsonElement>("/email", TestContext.Current.CancellationToken);
            var token = emails.EnumerateArray()
                .Where(email => email.GetProperty("to").EnumerateArray()
                    .Any(recipient => recipient.GetProperty("address").GetString() == to))
                .Select(email => link.Match(Body(email)))
                .Where(match => match.Success)
                .Select(match => Uri.UnescapeDataString(match.Groups[1].Value))
                .LastOrDefault();
            if (token is not null)
            {
                return token;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No email to {to} linking to {route} arrived in Maildev.");
    }

    /// <summary>The emails to <paramref name="to"/> that Maildev holds now, oldest first.</summary>
    public static async Task<IReadOnlyList<Email>> EmailsToAsync(string to)
    {
        var emails = await Http.GetFromJsonAsync<JsonElement>("/email", TestContext.Current.CancellationToken);
        return emails.EnumerateArray()
            .Where(email => email.GetProperty("to").EnumerateArray()
                .Any(recipient => recipient.GetProperty("address").GetString() == to))
            .Select(email => new Email(Text(email, "subject"), Lines(Text(email, "text")), Lines(Text(email, "html"))))
            .ToList();
    }

    /// <summary>Waits until <paramref name="to"/> has <paramref name="count"/> emails or more, and returns them.</summary>
    public static async Task<IReadOnlyList<Email>> WaitForEmailsAsync(string to, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var emails = await EmailsToAsync(to);
            if (emails.Count >= count)
            {
                return emails;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Fewer than {count} emails to {to} arrived in Maildev.");
    }

    /// <summary>
    /// The emails to <paramref name="to"/> once a second has passed, time enough for one the
    /// API sent to reach Maildev: both APIs send before they answer.
    /// </summary>
    public static async Task<IReadOnlyList<Email>> SettledEmailsToAsync(string to)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        return await EmailsToAsync(to);
    }

    /// <summary>SMTP carries line breaks as CRLF; the templates write LF.</summary>
    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Body(JsonElement email) =>
        $"{Text(email, "html")} {Text(email, "text")}";

    private static string Text(JsonElement email, string property) =>
        email.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
}
