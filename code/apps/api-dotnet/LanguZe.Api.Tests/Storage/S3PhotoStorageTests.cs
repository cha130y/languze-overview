using System.Net;
using Amazon.Runtime;
using LanguZe.Api.Modules.Storage.Contracts;
using LanguZe.Api.Modules.Storage.Shared;
using LanguZe.Api.Platform.Config;

namespace LanguZe.Api.Tests.Storage;

/// <summary>
/// What the SDK sends R2 when a photo is stored. SeaweedFS, which every other test stores
/// in, accepts what R2 refuses, so the request itself is read here.
/// </summary>
public sealed class S3PhotoStorageTests
{
    private static readonly StorageSettings R2 = new(
        new Uri("https://account.r2.cloudflarestorage.com"), "auto", "languze-photos", "access-key", "secret-key", TimeSpan.FromMinutes(15));

    // R2 refused production's first upload: "STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not implemented" (2026-11-06).
    [Fact]
    public async Task A_photo_is_sent_in_one_piece_not_in_streamed_signed_chunks()
    {
        var sent = new RecordingHandler();
        using var storage = new S3PhotoStorage(R2, new RecordingFactory(sent));

        await storage.StoreAsync(new PhotoUpload("photos/a.jpg", new byte[] { 1, 2, 3 }, "image/jpeg"), TestContext.Current.CancellationToken);

        var request = Assert.Single(sent.Requests);
        Assert.Equal((HttpMethod.Put, "/languze-photos/photos/a.jpg"), (request.Method, request.Path));
        Assert.Equal("UNSIGNED-PAYLOAD", request.ContentSha256);
        Assert.DoesNotContain("aws-chunked", request.ContentEncoding);
        Assert.Equal([1, 2, 3], request.Body);
    }

    private sealed record SentRequest(HttpMethod Method, string Path, string? ContentSha256, IReadOnlyList<string> ContentEncoding, byte[] Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("x-amz-content-sha256", out var sha) ? sha.Single() : null,
                [.. request.Content?.Headers.ContentEncoding ?? []],
                request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }

    private sealed class RecordingFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }
}
