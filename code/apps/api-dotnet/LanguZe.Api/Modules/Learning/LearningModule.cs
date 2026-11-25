using LanguZe.Api.Modules.Learning.Features.AnswerQuestion;
using LanguZe.Api.Modules.Learning.Features.GetCurrentSession;
using LanguZe.Api.Modules.Learning.Features.GetSession;
using LanguZe.Api.Modules.Learning.Features.StartSession;
using LanguZe.Api.Modules.Learning.Shared;

namespace LanguZe.Api.Modules.Learning;

/// <summary>
/// Playing a world and reviewing across them: sessions, answers, mastery and XP
/// (learning.module.ts; FR-030 to FR-036, FR-040, FR-050 to FR-053, FR-060).
/// </summary>
public static class LearningModule
{
    public static IServiceCollection AddLearningModule(this IServiceCollection services) =>
        services.AddScoped<SessionReader>()
            .AddScoped<StartSessionHandler>()
            .AddScoped<GetCurrentSessionHandler>()
            .AddScoped<GetSessionHandler>()
            .AddScoped<AnswerQuestionHandler>();

    public static IEndpointRouteBuilder MapLearningModule(this IEndpointRouteBuilder app)
    {
        GetCurrentSessionEndpoint.Map(app);
        StartSessionEndpoint.Map(app);
        GetSessionEndpoint.Map(app);
        AnswerQuestionEndpoint.Map(app);
        return app;
    }
}
