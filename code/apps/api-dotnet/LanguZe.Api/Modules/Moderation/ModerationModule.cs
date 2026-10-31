using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Moderation.Shared;

namespace LanguZe.Api.Modules.Moderation;

/// <summary>
/// Suspensions of AI features and the admin's work on them (moderation.module.ts). So far
/// only what other modules ask of it: a learner's AI access, the pause a blocked photo earns,
/// and telling the admins about a case that waits for them.
/// </summary>
public static class ModerationModule
{
    public static IServiceCollection AddModerationModule(this IServiceCollection services) =>
        services.AddScoped<IAiAccess, AiAccessReader>()
            .AddScoped<ISuspensions, SuspensionRaiser>()
            .AddScoped<IReviewNotifier, ReviewNotifier>();
}
