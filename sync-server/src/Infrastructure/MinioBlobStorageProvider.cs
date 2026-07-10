using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using SyncServer.Configuration;

namespace SyncServer.Infrastructure;

/// <summary>
/// Native MinIO implementation of <see cref="IBlobStorageProvider"/>, built on the
/// official MinIO .NET SDK. Selected when <c>ObjectStorage:Provider</c> is "Minio".
///
/// MinIO is also S3-compatible, so <see cref="S3BlobStorageProvider"/> can drive it too;
/// this provider exists so operators can opt into the first-class MinIO client and its
/// presign semantics without going through the AWS SDK.
///
/// <para>
/// Presigned URLs embed the host they were signed for. When the server talks to MinIO over
/// an internal address (e.g. <c>http://minio:9000</c> inside Docker) but clients reach it
/// over a different one, set <see cref="ObjectStorageSettings.PublicServiceUrl"/> to the
/// client-reachable URL — presigning then uses a second client bound to that endpoint while
/// metadata/delete operations keep using the internal endpoint.
/// </para>
/// </summary>
public class MinioBlobStorageProvider : IBlobStorageProvider
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _presignClient;
    private readonly string _bucketName;

    public MinioBlobStorageProvider(IOptions<ObjectStorageSettings> settings)
    {
        var s = settings.Value;
        _bucketName = s.BucketName;

        if (string.IsNullOrWhiteSpace(s.ServiceUrl))
            throw new InvalidOperationException(
                "ObjectStorage:ServiceUrl (or OBJECT_STORAGE_SERVICE_URL) is required for the MinIO provider, e.g. \"http://minio:9000\".");

        _client = BuildClient(s.ServiceUrl, s.AccessKey, s.SecretKey);

        // A dedicated client bound to the public endpoint, used only for presigning so the
        // signed host matches what clients will actually call. Falls back to the main client.
        _presignClient = string.IsNullOrWhiteSpace(s.PublicServiceUrl)
            ? _client
            : BuildClient(s.PublicServiceUrl, s.AccessKey, s.SecretKey);
    }

    private static IMinioClient BuildClient(string endpointUrl, string accessKey, string secretKey)
    {
        var uri = new Uri(endpointUrl);
        var secure = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);

        var builder = new MinioClient().WithEndpoint(uri.Host, uri.Port);
        if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
            builder = builder.WithCredentials(accessKey, secretKey);

        return builder.WithSSL(secure).Build();
    }

    public string GenerateUploadUrl(string key, string contentType, TimeSpan expiry)
    {
        var args = new PresignedPutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key)
            .WithExpiry((int)expiry.TotalSeconds);

        // Presigning is a local signature computation (no network round-trip), so blocking
        // here is safe and keeps the synchronous IBlobStorageProvider contract intact.
        return _presignClient.PresignedPutObjectAsync(args).GetAwaiter().GetResult();
    }

    public string GenerateDownloadUrl(string key, TimeSpan expiry)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key)
            .WithExpiry((int)expiry.TotalSeconds);

        return _presignClient.PresignedGetObjectAsync(args).GetAwaiter().GetResult();
    }

    public async Task<BlobMetadata?> GetObjectMetadataAsync(string key)
    {
        try
        {
            var stat = await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(key));

            return new BlobMetadata
            {
                ContentLength = stat.Size,
                ContentType = stat.ContentType ?? string.Empty
            };
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (BucketNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(string key)
    {
        await _client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key));
    }
}
