using Amazon.Runtime;
using Amazon.S3.Util;

namespace LanguZe.Api.Modules.Storage.Shared;

/// <summary>
/// Makes the bucket at startup if it is missing, as s3-photo-storage.ts's onModuleInit does,
/// so a fresh SeaweedFS needs no setup. A store that cannot be reached is logged, not fatal:
/// the API still starts, and only photos fail.
/// </summary>
internal sealed partial class PhotoBucketSetup(S3PhotoStorage storage, ILogger<PhotoBucketSetup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await AmazonS3Util.DoesS3BucketExistV2Async(storage.Client, storage.Bucket))
            {
                return;
            }

            await storage.Client.PutBucketAsync(storage.Bucket, cancellationToken);
            LogCreated(storage.Bucket);
        }
        catch (AmazonServiceException error)
        {
            LogUnreachable(storage.Bucket, error.Message);
        }
        catch (HttpRequestException error)
        {
            LogUnreachable(storage.Bucket, error.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created photo storage bucket \"{Bucket}\"")]
    private partial void LogCreated(string bucket);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo storage bucket \"{Bucket}\" is not reachable: {Reason}")]
    private partial void LogUnreachable(string bucket, string reason);
}
