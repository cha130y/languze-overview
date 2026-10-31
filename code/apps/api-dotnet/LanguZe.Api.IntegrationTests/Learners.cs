using System.Net;
using System.Net.Http.Headers;
using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace LanguZe.Api.IntegrationTests;

/// <summary>Learners and their worlds, as the tests of worlds and their analysis need them.</summary>
internal static class Learners
{
    /// <summary>Signs a learner up with a verified address, so the AI features are open to them; answers their browser.</summary>
    public static async Task<HttpClient> VerifiedAsync(ApiHost api, string email) => (await VerifiedLearnerAsync(api, email)).Browser;

    /// <summary>Signs a learner up with a verified address; answers their browser and account ID.</summary>
    public static async Task<(HttpClient Browser, Guid Id)> VerifiedLearnerAsync(ApiHost api, string email)
    {
        var browser = api.Browser();
        var learner = await ApiHost.SignUpAsync(browser, email);
        await using var db = api.Database.Context();
        await db.Users.Where(user => user.Id == learner)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailConfirmed, true), TestContext.Current.CancellationToken);
        return (browser, learner);
    }

    /// <summary>Creates a world and waits for its analysis; answers its ID.</summary>
    public static async Task<Guid> AnalysedWorldAsync(ApiHost api, HttpClient browser, byte[]? photo = null)
    {
        using var response = await PostWorldAsync(browser, photo ?? Photo(800, 600));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = Guid.Parse((await ApiHost.BodyAsync(response)).GetProperty("id").GetString()!);
        await api.Service<AnalysisRunner>().SettledAsync(id);
        return id;
    }

    public static Task<HttpResponseMessage> PostWorldAsync(HttpClient browser, byte[] photo)
    {
        var form = new MultipartFormDataContent { { new StringContent("ห้องนั่งเล่น"), "name" } };
        var file = new ByteArrayContent(photo);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "photo", "room.jpg");
        return browser.PostAsync(new Uri("/v1/worlds", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    /// <summary>A plain brown photo, which the fake provider describes as a living room.</summary>
    public static byte[] Photo(int width, int height)
    {
        using var black = Image.Black(width, height, bands: 3);
        using var image = black.NewFromImage(120, 90, 60).Cast(Enums.BandFormat.Uchar);
        return image.JpegsaveBuffer();
    }
}
