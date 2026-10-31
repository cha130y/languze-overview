using LanguZe.Api.Modules.Worlds.Features.CreateWorld;
using LanguZe.Api.Modules.Worlds.Features.DeleteWorld;
using LanguZe.Api.Modules.Worlds.Features.GetWorld;
using LanguZe.Api.Modules.Worlds.Features.GetWorldStatus;
using LanguZe.Api.Modules.Worlds.Features.ListWorlds;
using LanguZe.Api.Modules.Worlds.Features.RemoveWord;
using LanguZe.Api.Modules.Worlds.Features.RenameWorld;
using LanguZe.Api.Modules.Worlds.Features.RetryWorld;
using LanguZe.Api.Modules.Worlds.Jobs;
using LanguZe.Api.Modules.Worlds.Shared;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Modules.Worlds;

/// <summary>
/// Worlds, their photos, and the analysis that finds their words (worlds.module.ts;
/// FR-010–FR-027): creating, reading, renaming and deleting worlds, removing a word, retrying, and
/// releasing an analysis that never finished.
/// </summary>
public static class WorldsModule
{
    public static IServiceCollection AddWorldsModule(this IServiceCollection services, ApiSettings settings)
    {
        services.AddSingleton(settings.Limits);
        services.AddSingleton<AnalysisRunner>();
        services.AddSingleton<StaleAnalysis>();
        services.AddHostedService(provider => provider.GetRequiredService<StaleAnalysis>());
        services.AddScoped<AnalysisWork>();
        services.AddScoped<Analyses>();
        services.AddScoped<WorldReader>();
        services.AddScoped<CreateWorldHandler>();
        services.AddScoped<ListWorldsHandler>();
        services.AddScoped<RenameWorldHandler>();
        services.AddScoped<DeleteWorldHandler>();
        services.AddScoped<RemoveWordHandler>();
        services.AddScoped<RetryWorldHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapWorldsModule(this IEndpointRouteBuilder app)
    {
        ListWorldsEndpoint.Map(app);
        CreateWorldEndpoint.Map(app);
        GetWorldEndpoint.Map(app);
        GetWorldStatusEndpoint.Map(app);
        RenameWorldEndpoint.Map(app);
        RetryWorldEndpoint.Map(app);
        RemoveWordEndpoint.Map(app);
        DeleteWorldEndpoint.Map(app);
        return app;
    }
}
