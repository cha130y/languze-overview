using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Modules.Tutor.Features.ClearConversation;
using LanguZe.Api.Modules.Tutor.Features.GetConversation;
using LanguZe.Api.Modules.Tutor.Features.SendMessage;
using LanguZe.Api.Modules.Tutor.Shared;

namespace LanguZe.Api.Modules.Tutor;

/// <summary>The learner's one conversation with the AI tutor and its daily limit (tutor.module.ts; FR-070 to FR-076).</summary>
public static class TutorModule
{
    public static IServiceCollection AddTutorModule(this IServiceCollection services) =>
        services
            .AddScoped<TutorAllowance>()
            .AddScoped<ITutorUsage>(provider => provider.GetRequiredService<TutorAllowance>())
            .AddScoped<TutorTools>()
            .AddScoped<SendMessageHandler>()
            .AddScoped<GetConversationHandler>()
            .AddScoped<ClearConversationHandler>();

    public static IEndpointRouteBuilder MapTutorModule(this IEndpointRouteBuilder app)
    {
        GetConversationEndpoint.Map(app);
        SendMessageEndpoint.Map(app);
        ClearConversationEndpoint.Map(app);
        return app;
    }
}
