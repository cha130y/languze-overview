using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LanguZe.Api.Modules.Notifications.Shared;
using AspNet.Security.OAuth.Line;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// The API in process on a database of its own, with a clock the test moves: for what
/// tests/api-contract cannot reach from outside, such as a session a week old or an
/// account an admin suspended.
/// </summary>
internal sealed class ApiHost : IAsyncDisposable
{
    public const string Password = "correct-horse-42";

    public const string GoogleClientId = "test-google-client-id";

    public const string WebOrigin = "http://localhost:3003";


    private readonly WebApplicationFactory<Program> _factory;
    private readonly bool _ownsDatabase;

    private ApiHost(TemporaryDatabase database, string? dataProtectionKey, bool ownsDatabase, IReadOnlyDictionary<string, string>? settings = null)
    {
        Database = database;
        _ownsDatabase = ownsDatabase;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development")
                .UseSetting("DATABASE_URL", database.Url)
                // The fake provider, whatever the machine's own environment says.
                .UseSetting("GEMINI_API_KEY", string.Empty)
                .UseSetting("GOOGLE_CLIENT_ID", GoogleClientId)
                .UseSetting("GOOGLE_CLIENT_SECRET", "test-google-client-secret")
                .UseSetting("LINE_CLIENT_ID", "test-line-client-id")
                .UseSetting("LINE_CLIENT_SECRET", "test-line-client-secret")
                .UseSetting("FACEBOOK_CLIENT_ID", "test-facebook-client-id")
                .UseSetting("FACEBOOK_CLIENT_SECRET", "test-facebook-client-secret")
                .ConfigureTestServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddSingleton<IMailTransport>(Mail);
                    // After the handler's own post-configuration, which has made its backchannel already.
                    services.PostConfigure<GoogleOptions>("google", options => options.Backchannel = new HttpClient(Google, disposeHandler: false));
                    services.PostConfigure<LineAuthenticationOptions>("line", options => options.Backchannel = new HttpClient(Line, disposeHandler: false));
                    services.PostConfigure<FacebookOptions>("facebook", options => options.Backchannel = new HttpClient(Facebook, disposeHandler: false));
                });
            foreach (var (name, value) in settings ?? new Dictionary<string, string>())
            {
                host.UseSetting(name, value);
            }

            // Otherwise appsettings.Development.json's.
            if (dataProtectionKey is not null)
            {
                host.UseSetting("DATA_PROTECTION_KEY", dataProtectionKey);
            }
        });
    }

    public TemporaryDatabase Database { get; }

    public ManualClock Clock { get; } = new();

    /// <summary>Google's endpoints as this instance's Google handler reaches them.</summary>
    public FakeGoogle Google { get; } = new();

    public FakeLine Line { get; } = new();

    public FakeFacebook Facebook { get; } = new();

    /// <summary>The emails this instance sent, kept rather than sent.</summary>
    public RecordingMailTransport Mail { get; } = new();

    /// <summary>A database built as <c>pnpm db:dotnet</c> builds one, and the API on it, with any <paramref name="settings"/> a test needs.</summary>
    public static async Task<ApiHost> StartAsync(IReadOnlyDictionary<string, string>? settings = null)
    {
        var database = await TemporaryDatabase.WithPrismaSchemaAsync();
        await using (var db = database.Context())
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new ApiHost(database, dataProtectionKey: null, ownsDatabase: true, settings);
    }

    /// <summary>
    /// Another instance of the API on the same database, as a redeploy starts one: nothing
    /// cached, so it reads the Data Protection keys from the database again.
    /// </summary>
    public ApiHost Restart(string? dataProtectionKey = null) => new(Database, dataProtectionKey, ownsDatabase: false);

    /// <summary>One of this instance's services, such as a job a test runs itself.</summary>
    public T Service<T>()
        where T : notnull => _factory.Services.GetRequiredService<T>();

    /// <summary>A browser of the web app's: it keeps its cookies and sends the web app's origin.</summary>
    public HttpClient Browser()
    {
        // A redirect is the answer under test, not something to follow.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", WebOrigin);
        return client;
    }

    /// <summary>Signs a new learner up in <paramref name="browser"/> and answers their account ID.</summary>
    public static async Task<Guid> SignUpAsync(HttpClient browser, string email, string password = Password) =>
        (await BodyAsync(await SignUpResponseAsync(browser, email, password))).GetProperty("id").GetGuid();

    /// <summary>Signs a new learner up in <paramref name="browser"/>, answering the response with its session cookie.</summary>
    public static async Task<HttpResponseMessage> SignUpResponseAsync(HttpClient browser, string email, string password = Password)
    {
        var response = await browser.PostAsJsonAsync(
            "/v1/auth/sign-up",
            new { email, password, name = "Learner", birthYear = 2000, acceptTerms = true },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response;
    }

    /// <summary>
    /// A provider sign-in as the web app starts it and a browser comes back from it: the
    /// provider's own pages are skipped, and its fake answers the handler's backchannel.
    /// </summary>
    public static async Task<HttpResponseMessage> ProviderSignInAsync(HttpClient browser, string provider)
    {
        var start = await browser.PostAsJsonAsync(
            "/auth/sign-in/social",
            new { provider, callbackURL = $"{WebOrigin}/", newUserCallbackURL = $"{WebOrigin}/terms", errorCallbackURL = $"{WebOrigin}/sign-in" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var url = new Uri((await BodyAsync(start)).GetProperty("url").GetString()!);
        var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"]!;

        var callback = await browser.GetAsync(
            new Uri($"/auth/callback/{provider}?state={Uri.EscapeDataString(state)}&code=a-code-from-{provider}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return callback;
    }

    /// <summary>The session cookie a response set, as a browser sends it back.</summary>
    public static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith("languze.session=", StringComparison.Ordinal)).Split(';')[0];

    public static Task<HttpResponseMessage> SignInAsync(HttpClient browser, string email, string password = Password) =>
        browser.PostAsJsonAsync("/v1/auth/sign-in", new { email, password }, TestContext.Current.CancellationToken);

    public static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        return body.RootElement.Clone();
    }

    /// <summary>The error code of a refusal with <paramref name="status"/>.</summary>
    public static async Task<string?> ErrorAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("error").GetProperty("code").GetString();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (_ownsDatabase)
        {
            await Database.DisposeAsync();
        }
    }
}
