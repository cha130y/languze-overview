using Amazon.Runtime;
using Amazon.Util;
using Amazon.S3;
using Amazon.S3.Model;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Modules.Storage.Shared;

/// <summary>
/// Photo storage over S3, as s3-photo-storage.ts: Cloudflare R2 in production, SeaweedFS
/// locally. Path-style addresses, since neither serves a bucket as a host name, and
/// checksums only when an operation requires one: R2 and SeaweedFS do not accept every
/// checksum the SDK would otherwise add.
/// </summary>
internal sealed class S3PhotoStorage(StorageSettings settings) : IPhotoStorage, IDisposable
{
    private readonly AmazonS3Client _client = new(
        new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
        new AmazonS3Config
        {
            ServiceURL = settings.Endpoint.ToString(),
            AuthenticationRegion = settings.Region,
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });

    public string Bucket => settings.Bucket;

    public AmazonS3Client Client => _client;

    public async Task StoreAsync(PhotoUpload upload, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream(upload.Body.ToArray(), writable: false);
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = settings.Bucket,
                Key = upload.Key,
                InputStream = body,
                ContentType = upload.ContentType,
            },
            cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken)
    {
        using var answer = await _client.GetObjectAsync(settings.Bucket, key, cancellationToken);
        using var bytes = new MemoryStream();
        await answer.ResponseStream.CopyToAsync(bytes, cancellationToken);
        return bytes.ToArray();
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        await _client.DeleteObjectAsync(settings.Bucket, key, cancellationToken);

    /// <summary>
    /// A link to one object that stops working after a few minutes (P4). The SDK signs with
    /// its own clock and measures the expiry from it, so the expiry is counted from that clock too.
    /// </summary>
    public Task<string> SignedLinkAsync(string key) =>
        _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = settings.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = AWSSDKUtils.CorrectedUtcNow.Add(settings.PhotoLinkLifetime),
            Protocol = settings.Endpoint.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
        });

    public void Dispose() => _client.Dispose();
}
