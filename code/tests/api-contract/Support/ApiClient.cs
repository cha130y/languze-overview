using System.Net;
using System.Net.Http.Json;

namespace LanguZe.Api.Contract.Support;

/// <summary>
/// Talks to the API under test over HTTP, as the web app does, knowing nothing of how
/// it is built. Each client keeps its own cookies and stands for its own caller: the
/// API runs with one trusted proxy hop, so <c>X-Forwarded-For</c> gives every test a
/// separate address and the per-address rate limits never mix two tests together.
/// </summary>
public sealed class ApiClient : IDisposable
{
    public static readonly Uri BaseUrl = new(
        Environment.GetEnvironmentVariable("CONTRACT_API_URL") ?? "http://localhost:4001");

    /// <summary>The web app's origin, which requests that change data must carry.</summary>
    public static readonly string WebOrigin =
        Environment.GetEnvironmentVariable("CONTRACT_WEB_ORIGIN") ?? "http://localhost:3003";

    private readonly HttpClient _http;

    public ApiClient(string? callerAddress = null)
    {
        CallerAddress = callerAddress ?? FakeAddress.Next();
        _http = new HttpClient(new HttpClientHandler
        {
            CookieContainer = Cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
        })
        {
            BaseAddress = BaseUrl,
        };
    }

    public CookieContainer Cookies { get; } = new();

    public string CallerAddress { get; }

    public Task<HttpResponseMessage> GetAsync(string path) =>
        SendAsync(HttpMethod.Get, path, body: null, withOrigin: false);

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withOrigin = true) =>
        SendAsync(HttpMethod.Post, path, body, withOrigin);

    public Task<HttpResponseMessage> PostAsync(string path, object? body, string origin) =>
        SendAsync(HttpMethod.Post, path, body, withOrigin: true, origin);

    /// <summary>A PATCH with a JSON body.</summary>
    public Task<HttpResponseMessage> PatchAsync(string path, object body) =>
        SendAsync(HttpMethod.Patch, path, body, withOrigin: true);

    /// <summary>A DELETE without a body.</summary>
    public Task<HttpResponseMessage> DeleteAsync(string path) =>
        SendAsync(HttpMethod.Delete, path, body: null, withOrigin: true);

    /// <summary>A multipart form, as the web app uploads a photo.</summary>
    public Task<HttpResponseMessage> PostFormAsync(string path, MultipartFormDataContent form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Add("X-Forwarded-For", CallerAddress);
        request.Headers.Add("Origin", WebOrigin);
        return _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A DELETE with a JSON body, as deleting an account takes its confirmation.</summary>
    public Task<HttpResponseMessage> DeleteAsync(string path, object body) =>
        SendAsync(HttpMethod.Delete, path, body, withOrigin: true);

    /// <summary>
    /// A POST with the body exactly as given and the content type given, or none, to see
    /// how the API reads what the web app would never send.
    /// </summary>
    public Task<HttpResponseMessage> PostRawAsync(string path, string body, string? contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Forwarded-For", CallerAddress);
        request.Headers.Add("Origin", WebOrigin);
        request.Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
        if (contentType is not null)
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A GET with one extra header, such as a request ID a proxy would add.</summary>
    public Task<HttpResponseMessage> SendWithHeaderAsync(string path, string name, string value)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", CallerAddress);
        request.Headers.TryAddWithoutValidation(name, value);
        return _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        bool withOrigin,
        string? origin = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Forwarded-For", CallerAddress);
        if (withOrigin)
        {
            request.Headers.Add("Origin", origin ?? WebOrigin);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Addresses for callers that do not exist, one per client.</summary>
internal static class FakeAddress
{
    private static int _next = Random.Shared.Next(0, 1 << 20);

    public static string Next()
    {
        var n = Interlocked.Increment(ref _next) & 0xFFFFFF;
        return $"10.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{n & 0xFF}";
    }
}
