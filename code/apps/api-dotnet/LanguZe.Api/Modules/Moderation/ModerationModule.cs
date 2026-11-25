using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Moderation.Contracts;
using LanguZe.Api.Modules.Moderation.Features.ActOnLearner;
using LanguZe.Api.Modules.Moderation.Features.FindLearner;
using LanguZe.Api.Modules.Moderation.Features.GetAuditLog;
using LanguZe.Api.Modules.Moderation.Features.GetLearnerRecord;
using LanguZe.Api.Modules.Moderation.Features.GetReviewQueue;
using LanguZe.Api.Modules.Moderation.Shared;

namespace LanguZe.Api.Modules.Moderation;

/// <summary>
/// Suspensions of AI features and the admin's work on them (moderation.module.ts): a learner's
/// AI access, the pause a blocked photo earns, telling the admins about a case that waits for
/// them, and the admin area: the queue, search, moderation record, actions and audit log.
/// </summary>
public static class ModerationModule
{
    public static IServiceCollection AddModerationModule(this IServiceCollection services) =>
        services.AddScoped<IAiAccess, AiAccessReader>()
            .AddScoped<IAccountAiAccess, AccountAiAccessReader>()
            .AddScoped<ISuspensions, SuspensionRaiser>()
            .AddScoped<IReviewNotifier, ReviewNotifier>()
            .AddScoped<LearnerRecords>()
            .AddScoped<GetReviewQueueHandler>()
            .AddScoped<FindLearnerHandler>()
            .AddScoped<ActOnLearnerHandler>()
            .AddScoped<GetAuditLogHandler>();

    public static IEndpointRouteBuilder MapModerationModule(this IEndpointRouteBuilder app)
    {
        GetReviewQueueEndpoint.Map(app);
        FindLearnerEndpoint.Map(app);
        GetLearnerRecordEndpoint.Map(app);
        ActOnLearnerEndpoint.Map(app);
        GetAuditLogEndpoint.Map(app);
        return app;
    }
}
