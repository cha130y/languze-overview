using System.Net;
using LanguZe.Api.Contract.Support;

namespace LanguZe.Api.Contract;

/// <summary>
/// Renaming, deleting, removing a word and retrying (FR-015, FR-016, FR-025–FR-027,
/// US-013, US-014, US-021, US-022), on worlds the fake provider has analysed. A failed
/// world cannot be made from outside, since the test runner's fake always succeeds, so a
/// retry that runs is judged in process on each side, as is what a deletion takes from the
/// database (COVERAGE.md).
/// </summary>
[Trait("Slice", "Worlds")]
public sealed class WorldEditTests
{
    private const string MissingWorld = "00000000-0000-4000-8000-000000000000";

    [Fact]
    public async Task Renaming_answers_the_world_with_its_new_name()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-rename");
        var id = await ReadyWorldAsync(client);

        var response = await client.PatchAsync($"/v1/worlds/{id}", new { name = "ห้องครัวของแม่" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var world = await response.JsonAsync();
        Assert.Equal("ห้องครัวของแม่", world.GetProperty("name").GetString());
        Assert.Equal(6, world.GetProperty("words").GetArrayLength());
    }

    // V3: the same rule as creating a world.
    [Fact]
    public async Task A_name_that_breaks_the_rule_keeps_the_old_one()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-rename-bad");
        var id = await ReadyWorldAsync(client);

        var response = await client.PatchAsync($"/v1/worlds/{id}", new { name = "" });

        Assert.Equal(["name=isLength"], await response.ValidationFieldsAsync());
        Assert.Equal("ห้องครัว", (await (await client.GetAsync($"/v1/worlds/{id}")).JsonAsync()).GetProperty("name").GetString());
    }

    // ParseUUIDPipe runs on the route's ID before ValidationPipe reads the body.
    [Fact]
    public async Task A_bad_id_is_refused_before_the_body_is_read()
    {
        using var client = new ApiClient();
        await client.SignUpAsync("world-rename-order");

        var response = await client.PatchAsync("/v1/worlds/not-a-uuid", new { name = "" });

        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("BAD_REQUEST", error.Code);
        Assert.Equal("Validation failed (uuid is expected)", error.Message);
    }

    [Fact]
    public async Task Deleting_a_world_takes_it_from_the_list_and_answers_nothing()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-delete");
        var id = await ReadyWorldAsync(client);

        var response = await client.DeleteAsync($"/v1/worlds/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty((await (await client.GetAsync("/v1/worlds")).JsonAsync()).EnumerateArray());
        Assert.Equal("NOT_FOUND", (await (await client.GetAsync($"/v1/worlds/{id}")).ErrorAsync(HttpStatusCode.NotFound)).Code);
        Assert.Equal("NOT_FOUND", (await (await client.DeleteAsync($"/v1/worlds/{id}")).ErrorAsync(HttpStatusCode.NotFound)).Code);
    }

    [Fact]
    public async Task Removing_a_word_takes_it_from_the_world()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-remove-word");
        var id = await ReadyWorldAsync(client);
        var words = await WordIdsAsync(client, id);

        var response = await client.DeleteAsync($"/v1/worlds/{id}/words/{words[0]}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(words[1..], await WordIdsAsync(client, id));
    }

    // V5: a world with no words is nothing to practise, so the learner deletes it instead.
    [Fact]
    public async Task The_last_word_cannot_be_removed()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-last-word");
        var id = await ReadyWorldAsync(client);
        var words = await WordIdsAsync(client, id);
        foreach (var word in words[..^1])
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/v1/worlds/{id}/words/{word}")).StatusCode);
        }

        var response = await client.DeleteAsync($"/v1/worlds/{id}/words/{words[^1]}");

        var error = await response.ErrorAsync(HttpStatusCode.Conflict);
        Assert.Equal("LAST_WORD", error.Code);
        Assert.Equal("A world keeps at least one word. Delete the world instead.", error.Message);
        Assert.Equal([words[^1]], await WordIdsAsync(client, id));
    }

    [Fact]
    public async Task A_word_not_in_the_world_is_not_found()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-other-word");
        var first = await ReadyWorldAsync(client);
        var second = await ReadyWorldAsync(client);
        var wordOfSecond = (await WordIdsAsync(client, second))[0];

        var response = await client.DeleteAsync($"/v1/worlds/{first}/words/{wordOfSecond}");

        var error = await response.ErrorAsync(HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("This word is not in this world.", error.Message);
    }

    // FR-025: only a failed world can be retried, and an analysed one has nothing to retry.
    [Fact]
    public async Task A_world_that_has_not_failed_has_nothing_to_retry()
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-retry");
        var id = await ReadyWorldAsync(client);

        var response = await client.PostAsync($"/v1/worlds/{id}/retry");

        var error = await response.ErrorAsync(HttpStatusCode.Conflict);
        Assert.Equal("RETRY_NOT_AVAILABLE", error.Code);
        Assert.Equal("This world has nothing to retry.", error.Message);
    }

    // A retry is an analysis like any other, so it needs the same access, checked before the world is looked at.
    [Fact]
    public async Task An_unverified_account_cannot_retry_even_a_world_that_does_not_exist()
    {
        using var client = new ApiClient();
        await client.SignUpAsync("world-retry-unverified");

        var response = await client.PostAsync($"/v1/worlds/{MissingWorld}/retry");

        var error = await response.ErrorAsync(HttpStatusCode.Forbidden);
        Assert.Equal("NOT_VERIFIED", error.Code);
        Assert.Equal("Verify your email address to analyse photos.", error.Message);
    }

    // FR-008: another learner's world is NOT_FOUND for every change, and stays as it was.
    [Theory]
    [InlineData("PATCH", "")]
    [InlineData("DELETE", "")]
    [InlineData("DELETE", "/words/{word}")]
    [InlineData("POST", "/retry")]
    public async Task Another_learners_world_cannot_be_changed(string method, string suffix)
    {
        using var theirs = new ApiClient();
        await theirs.SignUpVerifiedAsync("world-edit-theirs");
        var id = await ReadyWorldAsync(theirs);
        var word = (await WordIdsAsync(theirs, id))[0];
        using var mine = new ApiClient();
        await mine.SignUpVerifiedAsync("world-edit-mine");
        var path = $"/v1/worlds/{id}{suffix.Replace("{word}", word, StringComparison.Ordinal)}";

        var response = method switch
        {
            "PATCH" => await mine.PatchAsync(path, new { name = "ของฉัน" }),
            "DELETE" => await mine.DeleteAsync(path),
            _ => await mine.PostAsync(path),
        };

        var error = await response.ErrorAsync(HttpStatusCode.NotFound);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("This world does not exist.", error.Message);
        var world = await (await theirs.GetAsync($"/v1/worlds/{id}")).JsonAsync();
        Assert.Equal("ห้องครัว", world.GetProperty("name").GetString());
        Assert.Equal(6, world.GetProperty("words").GetArrayLength());
    }

    [Theory]
    [InlineData("PATCH", "/v1/worlds/not-a-uuid")]
    [InlineData("DELETE", "/v1/worlds/not-a-uuid")]
    [InlineData("POST", "/v1/worlds/not-a-uuid/retry")]
    [InlineData("DELETE", "/v1/worlds/not-a-uuid/words/00000000-0000-4000-8000-000000000000")]
    [InlineData("DELETE", "/v1/worlds/00000000-0000-4000-8000-000000000000/words/not-a-uuid")]
    public async Task An_id_that_is_not_an_id_is_refused(string method, string path)
    {
        using var client = new ApiClient();
        await client.SignUpVerifiedAsync("world-edit-bad-id");

        var response = method switch
        {
            "PATCH" => await client.PatchAsync(path, new { name = "ห้อง" }),
            "DELETE" => await client.DeleteAsync(path),
            _ => await client.PostAsync(path),
        };

        var error = await response.ErrorAsync(HttpStatusCode.BadRequest);
        Assert.Equal("BAD_REQUEST", error.Code);
        Assert.Equal("Validation failed (uuid is expected)", error.Message);
    }

    [Theory]
    [InlineData("PATCH", "")]
    [InlineData("DELETE", "")]
    [InlineData("DELETE", "/words/00000000-0000-4000-8000-000000000000")]
    [InlineData("POST", "/retry")]
    public async Task Without_a_session_no_world_can_be_changed(string method, string suffix)
    {
        using var client = new ApiClient();
        var path = $"/v1/worlds/{MissingWorld}{suffix}";

        var response = method switch
        {
            "PATCH" => await client.PatchAsync(path, new { name = "ห้อง" }),
            "DELETE" => await client.DeleteAsync(path),
            _ => await client.PostAsync(path),
        };

        Assert.Equal("NOT_SIGNED_IN", (await response.ErrorAsync(HttpStatusCode.Unauthorized)).Code);
    }

    /// <summary>A world named ห้องครัว whose analysis has finished, with the fake provider's six words.</summary>
    internal static async Task<string> ReadyWorldAsync(ApiClient client)
    {
        var id = (await (await WorldsTests.CreateAsync(client, "ห้องครัว")).JsonAsync()).GetProperty("id").GetString()!;
        await WorldsTests.ReadyAsync(client, id);
        return id;
    }

    private static async Task<string[]> WordIdsAsync(ApiClient client, string world) =>
        [.. (await (await client.GetAsync($"/v1/worlds/{world}")).JsonAsync()).GetProperty("words").EnumerateArray().Select(word => word.GetProperty("id").GetString()!)];
}
