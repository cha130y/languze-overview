using LanguZe.Api.Modules.Ai;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Platform.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LanguZe.Api.Tests.Ai;

/// <summary>ai.module.ts: Gemini once a key is given, the fake provider otherwise (B4).</summary>
public sealed class AiModuleTests
{
    [Theory]
    [InlineData("a-key", "gemini")]
    [InlineData("", "fake")]
    public void The_provider_is_gemini_once_a_key_is_given(string key, string provider)
    {
        var settings = ApiSettings.Load(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://languze:languze@localhost:5433/languze",
                ["DATA_PROTECTION_KEY"] = Convert.ToBase64String(new byte[32]),
                ["GEMINI_API_KEY"] = key,
            }).Build(),
            isProduction: false);
        var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System).AddAiModule(settings);
        using var built = services.BuildServiceProvider();

        Assert.Equal(provider, built.GetRequiredService<IAiProvider>().Name);
    }
}
