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
internal sealed class S3PhotoStorage : IPhotoStorage, IDisposable
{
    private readonly StorageSettings _settings;
    private readonly AmazonS3Client _client;

    public S3PhotoStorage(StorageSettings settings)
        : this(settings, http: null)
    {
    }

    /// <summary>With the HTTP the SDK sends through, for a test that reads what it sent.</summary>
    internal S3PhotoStorage(StorageSettings settings, HttpClientFactory? http)
    {
        _settings = settings;
        var config = new AmazonS3Config
        {
            ServiceURL = settings.Endpoint.ToString(),
            AuthenticationRegion = settings.Region,
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (http is not null)
        {
            config.HttpClientFactory = http;
        }

        _client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), config);
    }

    public string Bucket => _settings.Bucket;

    public AmazonS3Client Client => _client;

    /// <summary>
    /// The body is signed as a whole rather than in streamed chunks: R2 refuses the SDK's
    /// streaming SigV4 (Cloudflare's R2 guide for AWSSDK.S3), which SeaweedFS accepts, so only
    /// production's first upload showed it (2026-11-06). Over HTTPS the body goes unsigned, as
    /// that guide asks; TLS keeps it whole. The SDK allows that only over HTTPS, so SeaweedFS,
    /// on plain HTTP locally, keeps the default.
    /// </summary>
    public async Task StoreAsync(PhotoUpload upload, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream(upload.Body.ToArray(), writable: false);
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _settings.Bucket,
                Key = upload.Key,
                InputStream = body,
                ContentType = upload.ContentType,
                DisablePayloadSigning = _settings.Endpoint.Scheme == Uri.UriSchemeHttps,
                DisableDefaultChecksumValidation = true,
            },
            cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken)
    {
        using var answer = await _client.GetObjectAsync(_settings.Bucket, key, cancellationToken);
        using var bytes = new MemoryStream();
        await answer.ResponseStream.CopyToAsync(bytes, cancellationToken);
        return bytes.ToArray();
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        await _client.DeleteObjectAsync(_settings.Bucket, key, cancellationToken);

    /// <summary>
    /// A link to one object that stops working after a few minutes (P4). The SDK signs with
    /// its own clock and measures the expiry from it, so the expiry is counted from that clock too.
    /// </summary>
    public Task<string> SignedLinkAsync(string key) =>
        _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = AWSSDKUtils.CorrectedUtcNow.Add(_settings.PhotoLinkLifetime),
            Protocol = _settings.Endpoint.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
        });

    public void Dispose() => _client.Dispose();
}
