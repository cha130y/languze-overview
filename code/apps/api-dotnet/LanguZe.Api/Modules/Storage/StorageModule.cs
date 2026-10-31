using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Storage.Jobs;
using LanguZe.Api.Modules.Storage.Shared;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Modules.Storage;

/// <summary>
/// Prepared photos and the object store that holds them (storage.module.ts; A3, FR-017,
/// NFR-009). The module owns the files; the rows that point at them belong to the modules
/// that create them, which is why those transactions write the cleanup records and only
/// this module removes them.
/// </summary>
public static class StorageModule
{
    public static IServiceCollection AddStorageModule(this IServiceCollection services, ApiSettings settings)
    {
        services.AddSingleton(settings.Storage);
        services.AddSingleton<S3PhotoStorage>();
        services.AddSingleton<IPhotoStorage>(provider => provider.GetRequiredService<S3PhotoStorage>());
        services.AddHostedService<PhotoBucketSetup>();
        services.AddSingleton<PhotoPreparation>();

        services.AddSingleton<PhotoCleanup>();
        services.AddSingleton<IPhotoCleanup>(provider => provider.GetRequiredService<PhotoCleanup>());
        services.AddHostedService(provider => provider.GetRequiredService<PhotoCleanup>());
        return services;
    }
}
