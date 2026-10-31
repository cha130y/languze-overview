using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LanguZe.Api.Contract.Support;

/// <summary>The error format every failure uses: <c>{ "error": { code, message, details? } }</c>.</summary>
public sealed record ApiError(string Code, string Message, JsonElement? Details);

public static class Responses
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    /// <summary>Asserts the status and returns the error in the shared format.</summary>
    public static async Task<ApiError> ErrorAsync(this HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var error = (await response.JsonAsync()).GetProperty("error");
        var message = error.GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message), "An error carries a message for developers.");
        return new ApiError(
            error.GetProperty("code").GetString()!,
            message!,
            error.TryGetProperty("details", out var details) ? details : null);
    }

    /// <summary>Asserts VALIDATION_FAILED and lists each field it names as <c>field=rule,rule</c>, in the API's order.</summary>
    public static async Task<string[]> ValidationFieldsAsync(this HttpResponseMessage response)
    {
        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Equal("The request did not pass validation.", error.Message);
        return error.Details!.Value.GetProperty("fields").EnumerateArray()
            .Select(field => $"{field.GetProperty("field").GetString()}={string.Join(',', field.GetProperty("rules").EnumerateArray().Select(rule => rule.GetString()))}")
            .ToArray();
    }

    public static IReadOnlyList<string> SetCookies(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    /// <summary>
    /// The session cookie, recognised by what it must be rather than by its name, which
    /// differs between implementations (ADR-0006, section 5).
    /// </summary>
    public static string SessionCookie(this HttpResponseMessage response)
    {
        var cookies = response.SetCookies();
        var session = cookies.FirstOrDefault(IsSessionCookie);
        Assert.True(
            session is not null,
            $"Expected an HttpOnly, SameSite=Lax session cookie; got: {string.Join(" | ", cookies)}");
        return session!;
    }

    private static bool IsSessionCookie(string cookie)
    {
        var attributes = Attributes(cookie);
        return attributes.ContainsKey("httponly")
            && attributes.TryGetValue("samesite", out var sameSite)
            && sameSite.Equals("lax", StringComparison.OrdinalIgnoreCase)
            && attributes.TryGetValue("path", out var path)
            && path == "/"
            && Lifetime(cookie) is { } lifetime
            && lifetime > TimeSpan.Zero;
    }

    /// <summary>How long the cookie lives, from <c>Max-Age</c> or <c>Expires</c>.</summary>
    public static TimeSpan? Lifetime(string cookie)
    {
        var attributes = Attributes(cookie);
        if (attributes.TryGetValue("max-age", out var maxAge)
            && int.TryParse(maxAge, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (attributes.TryGetValue("expires", out var expires)
            && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return at - DateTimeOffset.UtcNow;
        }

        return null;
    }

    private static Dictionary<string, string> Attributes(string cookie) =>
        cookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => pair[0].ToLowerInvariant(),
                pair => pair.Length > 1 ? pair[1] : string.Empty);
}
