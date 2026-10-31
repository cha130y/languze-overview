using Google.GenAI;
using LanguZe.Api.Modules.Ai.Contracts;
using LanguZe.Api.Modules.Ai.Shared;
using LanguZe.Api.Modules.Ai.Shared.Gemini;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Modules.Ai;

/// <summary>
/// The AI interface and the provider behind it (ai.module.ts; AIR-001, ADR-0004). Only this
/// module knows which provider answers; everything else depends on <see cref="IAiProvider"/>.
/// Gemini answers once a key is given, as production must; the fake provider otherwise, so
/// development and tests cost nothing and give the same answer every time (B4).
/// </summary>
public static partial class AiModule
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, ApiSettings settings)
    {
        services.AddSingleton(settings.FakeAi);
        services.AddSingleton<FakeAiProvider>();
        if (settings.Gemini.IsConfigured)
        {
            services.AddSingleton(settings.Gemini);
            services.AddSingleton(_ => new Client(apiKey: settings.Gemini.ApiKey));
            services.AddSingleton<IGeminiModels, GeminiModels>();
        }

        services.AddSingleton<IAiProvider>(provider =>
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiModule));
            if (settings.Gemini.IsConfigured)
            {
                LogGemini(logger);
                return new GeminiAiProvider(provider.GetRequiredService<IGeminiModels>(), settings.Gemini);
            }

            // Lets a developer see a blocked photo or a provider error in the browser, and take
            // long enough over it to watch the page wait (FR-021).
            var fake = provider.GetRequiredService<FakeAiProvider>();
            LogFake(logger, fake.Behaviour, (int)settings.FakeAi.Delay.TotalMilliseconds);
            return fake;
        });
        services.AddSingleton<AiCallRecorder>();
        return services;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Photo analysis and the tutor use Gemini")]
    private static partial void LogGemini(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Photo analysis and the tutor use the fake provider ({Behaviour}, {DelayMs} ms)")]
    private static partial void LogFake(ILogger logger, string behaviour, int delayMs);
}
