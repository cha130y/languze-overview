using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LanguZe.Api.Contract.Support;

namespace LanguZe.Api.Contract;

/// <summary>
/// Creating a world from a photo and reading it back (FR-010–FR-014, FR-017, FR-021,
/// US-010–US-012). The test runner gives the API the fake AI provider, which finds the
/// same six words in every photo at once, so what a learner ends up with can be asserted.
/// What only a database shows, the twenty-world limit and the daily analyses among it, is
/// judged in process on each side (COVERAGE.md).
/// </summary>
[Trait("Slice", "Worlds")]
public sealed class WorldsTests
{
    private static readonly byte[] Photo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "photo-3000x2000.jpg"));

    /// <summary>The words the fake provider finds in every photo, in its order, as a world's page lists them.</summary>
    private static readonly string[] FakeWords =
    [
        "sofa|โซฟา|We sit on the sofa every evening.|A1|0.05,0.45,0.4,0.35",
        "curtain|ผ้าม่าน|Please open the curtain in the morning.|A2|0.55,0.05,0.3,0.6",
        "coffee table|โต๊ะกลาง|My book is on the coffee table.|A2|0.2,0.7,0.25,0.18",
        "rug|พรม|The rug under the table is soft.|B1|0.12,0.78,0.5,0.18",
        "plant|ต้นไม้ในกระถาง|I water the plant on Sunday.|A1|0.72,0.55,0.18,0.3",
        "clock|นาฬิกาแขวน|The clock on the wall is slow.|A1|0.78,0.08,0.12,0.12",
    ];

    [Fact]
    public async Task Creating_a_world_answers_it_at_once_while_the_photo_is_analysed()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-create");

        var response = await CreateAsync(client, "ห้องครัว");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var world = await response.JsonAsync();
        Assert.Equal(
            ["id", "name", "status", "failureReason", "thumbnailUrl", "wordCount", "masteredCount", "createdAt", "photoUrl", "words"],
            world.EnumerateObject().Select(property => property.Name));
        Assert.True(Guid.TryParse(world.GetProperty("id").GetString(), out _));
        Assert.Equal("ห้องครัว", world.GetProperty("name").GetString());
        // The words come from a background analysis, so the answer comes first (FR-021).
        Assert.Equal("ANALYZING", world.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, world.GetProperty("failureReason").ValueKind);
        Assert.Contains("X-Amz-Signature", world.GetProperty("photoUrl").GetString(), StringComparison.Ordinal);
        Assert.Contains("X-Amz-Signature", world.GetProperty("thumbnailUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, world.GetProperty("wordCount").GetInt32());
        Assert.Equal(0, world.GetProperty("masteredCount").GetInt32());
        Assert.Empty(world.GetProperty("words").EnumerateArray());
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$", world.GetProperty("createdAt").GetString());
    }

    // FR-017, V18: what is stored and served is the prepared photo, never the upload.
    [Fact]
    public async Task The_photo_is_served_through_its_signed_link_at_most_2048_pixels_with_a_320_pixel_thumbnail()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-photo");
        var world = await (await CreateAsync(client, "ห้องครัว")).JsonAsync();

        using var http = new HttpClient();
        using var photo = await http.GetAsync(new Uri(world.GetProperty("photoUrl").GetString()!), TestContext.Current.CancellationToken);
        using var thumbnail = await http.GetAsync(new Uri(world.GetProperty("thumbnailUrl").GetString()!), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal((2048, 1365), JpegSize(await photo.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)));
        Assert.Equal((320, 213), JpegSize(await thumbnail.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)));
    }

    // FR-011: the type is decided by the content, so a file named kitchen.jpg is still refused.
    [Fact]
    public async Task A_file_that_is_not_a_photo_is_refused_and_leaves_no_world()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-not-photo");

        var response = await CreateAsync(client, "ห้องครัว", "not a photo at all"u8.ToArray());

        var error = await response.ErrorAsync(HttpStatusCode.UnsupportedMediaType);
        Assert.Equal("PHOTO_TYPE_NOT_ALLOWED", error.Code);
        Assert.Equal("This file could not be read as a photo.", error.Message);
        Assert.Empty((await (await client.GetAsync("/v1/worlds")).JsonAsync()).EnumerateArray());
    }

    // Security review: the form is one field and one file, and nothing more is read.
    [Fact]
    public async Task A_form_with_more_than_a_name_and_a_photo_is_refused_while_it_is_read()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-extra");
        using var form = Form("ห้องครัว", Photo);
        form.Add(new StringContent("x"), "extra");

        var response = await client.PostFormAsync("/v1/worlds", form);

        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("BAD_REQUEST", error.Code);
        Assert.Equal("Too many fields", error.Message);
        Assert.Empty((await (await client.GetAsync("/v1/worlds")).JsonAsync()).EnumerateArray());
    }

    // multer's own limits, each stopping the form while it is read: its words are the message.
    [Theory]
    [InlineData("second photo", "Too many files")]
    [InlineData("file under another name", "Unexpected file field - picture")]
    [InlineData("photo as text", "Too many fields")]
    [InlineData("long name", "Field value too long - name")]
    [InlineData("file first, then two fields", "Too many fields")]
    public async Task Each_of_the_forms_limits_is_refused_in_multers_words(string form, string message)
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-limits");
        using var content = new MultipartFormDataContent();
        switch (form)
        {
            case "second photo":
                content.Add(new StringContent("ห้องครัว"), "name");
                content.Add(PhotoContent(Photo), "photo", "kitchen.jpg");
                content.Add(PhotoContent(Photo), "photo", "again.jpg");
                break;
            case "file under another name":
                content.Add(new StringContent("ห้องครัว"), "name");
                content.Add(PhotoContent(Photo), "picture", "kitchen.jpg");
                break;
            case "photo as text":
                content.Add(new StringContent("ห้องครัว"), "name");
                content.Add(new StringContent("kitchen"), "photo");
                break;
            case "long name":
                content.Add(new StringContent(new string('a', 1025)), "name");
                content.Add(PhotoContent(Photo), "photo", "kitchen.jpg");
                break;
            default:
                content.Add(PhotoContent(Photo), "photo", "kitchen.jpg");
                content.Add(new StringContent("ห้องครัว"), "name");
                content.Add(new StringContent("x"), "extra");
                break;
        }

        var response = await client.PostFormAsync("/v1/worlds", content);

        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("BAD_REQUEST", error.Code);
        Assert.Equal(message, error.Message);
        Assert.Empty((await (await client.GetAsync("/v1/worlds")).JsonAsync()).EnumerateArray());
    }

    [Fact]
    public async Task A_photo_over_10_mb_is_refused()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-large");

        var response = await CreateAsync(client, "ใหญ่ไป", Enumerable.Repeat((byte)1, 11 * 1024 * 1024).ToArray());

        var error = await response.ErrorAsync(HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal("PHOTO_TOO_LARGE", error.Code);
        Assert.Equal("This photo is larger than 10 MB.", error.Message);
    }

    // V3: a world's name is 1 to 50 characters.
    [Theory]
    [InlineData("")]
    [InlineData("กกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกกก")]
    public async Task A_name_outside_1_to_50_characters_is_refused(string name)
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-name");

        var response = await CreateAsync(client, name);

        Assert.Equal(["name=isLength"], await response.ValidationFieldsAsync());
    }

    [Fact]
    public async Task A_form_without_a_name_breaks_both_rules_on_it()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-no-name");
        using var form = new MultipartFormDataContent();
        form.Add(PhotoContent(Photo), "photo", "kitchen.jpg");

        var response = await client.PostFormAsync("/v1/worlds", form);

        Assert.Equal(["name=isLength,isString"], await response.ValidationFieldsAsync());
    }

    // A service's own refusal of a field: details.fields is an object of field to message.
    [Fact]
    public async Task A_form_without_a_photo_names_the_photo()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-no-photo");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("ห้องครัว"), "name");

        var response = await client.PostFormAsync("/v1/worlds", form);

        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Equal("A photo is required.", error.Message);
        Assert.Equal("""{"fields":{"photo":"A photo is required."}}""", error.Details!.Value.GetRawText());
    }

    // FR-006: photo analysis is an AI feature, so the address has to be verified first.
    [Fact]
    public async Task An_unverified_account_is_refused()
    {
        using var client = new ApiClient();
        await client.SignUpAsync("world-unverified");

        var response = await CreateAsync(client, "ห้องครัว");

        var error = await response.ErrorAsync(HttpStatusCode.Forbidden);
        Assert.Equal("NOT_VERIFIED", error.Code);
        Assert.Equal("Verify your email address to analyse photos.", error.Message);
    }

    // FR-013, FR-014, FR-022: once the analysis has finished, the list and the world report its words.
    [Fact]
    public async Task The_list_shows_the_learners_worlds_newest_first_and_a_world_its_words()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-list");
        var older = (await (await CreateAsync(client, "ห้องครัว")).JsonAsync()).GetProperty("id").GetString()!;
        var newer = (await (await CreateAsync(client, "โต๊ะทำงาน")).JsonAsync()).GetProperty("id").GetString()!;
        await ReadyAsync(client, older);
        await ReadyAsync(client, newer);

        var list = (await (await client.GetAsync("/v1/worlds")).JsonAsync()).EnumerateArray().ToList();

        Assert.Equal(["โต๊ะทำงาน", "ห้องครัว"], list.Select(world => world.GetProperty("name").GetString()));
        Assert.Equal(
            ["id", "name", "status", "failureReason", "thumbnailUrl", "wordCount", "masteredCount", "createdAt"],
            list[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal("READY", list[0].GetProperty("status").GetString());
        Assert.Contains("X-Amz-Signature", list[0].GetProperty("thumbnailUrl").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, list[0].GetProperty("wordCount").GetInt32());
        Assert.Equal(0, list[0].GetProperty("masteredCount").GetInt32());

        var world = await (await client.GetAsync($"/v1/worlds/{newer}")).JsonAsync();
        var words = world.GetProperty("words").EnumerateArray().ToList();
        Assert.Equal(["id", "english", "thaiMeaning", "exampleSentence", "cefrLevel", "box", "mastery"], words[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(["x", "y", "width", "height"], words[0].GetProperty("box").EnumerateObject().Select(property => property.Name));
        Assert.Equal(FakeWords, words.Select(Describe));
        Assert.All(words, word => Assert.Equal("NEW", word.GetProperty("mastery").GetString()));
        Assert.Equal(6, words.Select(word => word.GetProperty("id").GetString()).Distinct().Count());
    }

    // FR-021, P3: the page that waits asks for the status alone.
    [Fact]
    public async Task The_status_is_answered_on_its_own_until_the_world_is_ready()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-status");
        var id = (await (await CreateAsync(client, "ห้องครัว")).JsonAsync()).GetProperty("id").GetString()!;

        await ReadyAsync(client, id);

        Assert.Equal("""{"status":"READY","failureReason":null}""", await (await client.GetAsync($"/v1/worlds/{id}/status")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // FR-008: another learner's world is NOT_FOUND, so the API never confirms it exists.
    [Theory]
    [InlineData("")]
    [InlineData("/status")]
    public async Task Another_learners_world_is_not_found(string suffix)
    {
        using var theirs = new ApiClient();
        await theirs.SignUpVerifiedAsync("world-theirs");
        var id = (await (await CreateAsync(theirs, "ห้องครัว")).JsonAsync()).GetProperty("id").GetString()!;
        using var mine = new ApiClient();
        await mine.SignUpAsync("world-mine");

        var response = await mine.GetAsync($"/v1/worlds/{id}{suffix}");

        var error = await response.ErrorAsync(HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("This world does not exist.", error.Message);
        Assert.Empty((await (await mine.GetAsync("/v1/worlds")).JsonAsync()).EnumerateArray());
    }

    [Theory]
    [InlineData("/v1/worlds/not-a-uuid")]
    [InlineData("/v1/worlds/not-a-uuid/status")]
    public async Task A_world_id_that_is_not_an_id_is_refused(string path)
    {
        using var client = new ApiClient();
        await client.SignUpAsync("world-bad-id");

        var response = await client.GetAsync(path);

        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("BAD_REQUEST", error.Code);
        Assert.Equal("Validation failed (uuid is expected)", error.Message);
    }

    [Theory]
    [InlineData("GET", "/v1/worlds")]
    [InlineData("POST", "/v1/worlds")]
    [InlineData("GET", "/v1/worlds/00000000-0000-4000-8000-000000000000")]
    [InlineData("GET", "/v1/worlds/00000000-0000-4000-8000-000000000000/status")]
    public async Task Without_a_session_every_world_route_is_NOT_SIGNED_IN(string method, string path)
    {
        using var client = new ApiClient();

        var response = method == "GET" ? await client.GetAsync(path) : await client.PostFormAsync(path, Form("ห้องครัว", Photo));

        Assert.Equal("NOT_SIGNED_IN", (await response.ErrorAsync(HttpStatusCode.Unauthorized)).Code);
    }

    internal static Task<HttpResponseMessage> CreateAsync(ApiClient client, string name, byte[]? photo = null) =>
        client.PostFormAsync("/v1/worlds", Form(name, photo ?? Photo));

    /// <summary>Waits for the background analysis, as the web app's waiting page does.</summary>
    internal static async Task ReadyAsync(ApiClient client, string id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var status = (await (await client.GetAsync($"/v1/worlds/{id}/status")).JsonAsync()).GetProperty("status").GetString();
            if (status != "ANALYZING")
            {
                Assert.Equal("READY", status);
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"World {id} was still being analysed after 20 seconds.");
    }

    private static MultipartFormDataContent Form(string name, byte[] photo)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(PhotoContent(photo), "photo", "kitchen.jpg");
        return form;
    }

    private static ByteArrayContent PhotoContent(byte[] photo)
    {
        var content = new ByteArrayContent(photo);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return content;
    }

    private static readonly string[] BoxSides = ["x", "y", "width", "height"];

    private static string Describe(JsonElement word)
    {
        var box = word.GetProperty("box");
        var corners = string.Join(',', BoxSides.Select(side => box.GetProperty(side).GetDouble().ToString(CultureInfo.InvariantCulture)));
        return $"{word.GetProperty("english").GetString()}|{word.GetProperty("thaiMeaning").GetString()}|{word.GetProperty("exampleSentence").GetString()}|{word.GetProperty("cefrLevel").GetString()}|{corners}";
    }

    /// <summary>A JPEG's width and height, from its start-of-frame marker.</summary>
    private static (int Width, int Height) JpegSize(byte[] jpeg)
    {
        var at = 2;
        while (at + 9 < jpeg.Length)
        {
            var marker = jpeg[at + 1];
            var length = (jpeg[at + 2] << 8) | jpeg[at + 3];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                return ((jpeg[at + 7] << 8) | jpeg[at + 8], (jpeg[at + 5] << 8) | jpeg[at + 6]);
            }

            at += 2 + length;
        }

        throw new InvalidDataException("No start-of-frame marker.");
    }
}
