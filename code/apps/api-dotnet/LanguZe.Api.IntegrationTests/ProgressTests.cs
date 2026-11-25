using LanguZe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.IntegrationTests;

/// <summary>
/// Progress where only the database can set it up, as progress.e2e-spec.ts does through Prisma:
/// a word MASTERED, which takes answers on more than one day (FR-061).
/// </summary>
public sealed class ProgressTests
{
    [Fact]
    public async Task A_mastered_word_is_counted_in_the_words_and_in_its_world()
    {
        await using var api = await ApiHost.StartAsync();
        var (browser, learner) = await Learners.VerifiedLearnerAsync(api, "progress-mastered@example.com");
        var world = await Learners.AnalysedWorldAsync(api, browser);
        await using (var db = api.Database.Context())
        {
            var words = await db.WordOccurrences.Where(row => row.WorldId == world).Select(row => row.VocabularyWordId).Take(2).ToListAsync(TestContext.Current.CancellationToken);
            db.WordMastery.Add(new WordMastery { VocabularyWordId = words[0], LearnerId = learner, Level = MasteryLevel.Learning, LastPractisedAt = api.Clock.GetUtcNow() });
            db.WordMastery.Add(new WordMastery { VocabularyWordId = words[1], LearnerId = learner, Level = MasteryLevel.Mastered, LastPractisedAt = api.Clock.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var progress = await ApiHost.BodyAsync(await browser.GetAsync(new Uri("/v1/progress", UriKind.Relative), TestContext.Current.CancellationToken));

        var counts = progress.GetProperty("words");
        Assert.Equal((4, 1, 0, 1), (counts.GetProperty("NEW").GetInt32(), counts.GetProperty("LEARNING").GetInt32(), counts.GetProperty("FAMILIAR").GetInt32(), counts.GetProperty("MASTERED").GetInt32()));
        var only = Assert.Single(progress.GetProperty("worlds").EnumerateArray());
        Assert.Equal((6, 1), (only.GetProperty("wordCount").GetInt32(), only.GetProperty("masteredCount").GetInt32()));
    }
}
