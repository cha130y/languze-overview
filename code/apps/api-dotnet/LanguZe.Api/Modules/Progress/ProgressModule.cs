using LanguZe.Api.Modules.Progress.Features.GetProgress;

namespace LanguZe.Api.Modules.Progress;

/// <summary>What a learner has to show for their practice (progress.module.ts; FR-060, FR-061).</summary>
public static class ProgressModule
{
    public static IServiceCollection AddProgressModule(this IServiceCollection services) =>
        services.AddScoped<GetProgressHandler>();

    public static IEndpointRouteBuilder MapProgressModule(this IEndpointRouteBuilder app)
    {
        GetProgressEndpoint.Map(app);
        return app;
    }
}
