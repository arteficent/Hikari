using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SyncServer.Configuration;

namespace SyncServer.Infrastructure;

/// <summary>
/// AWS S3 implementation of <see cref="IBlobStorageProvider"/>.
/// Also works with any S3-compatible API (Cloudflare R2, MinIO, DigitalOcean Spaces, etc.)
/// by pointing the S3 client at a custom endpoint.
/// </summary>
public class S3BlobStorageProvider : IBlobStorageProvider
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public S3BlobStorageProvider(IAmazonS3 s3, IOptions<CloudStorageSettings> settings)
    {
        _s3 = s3;
        _bucketName = settings.Value.BucketName;
    }

    public string GenerateUploadUrl(string key, string contentType, TimeSpan expiry)
    {
        return _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiry),
            ContentType = contentType
        });
    }

    public string GenerateDownloadUrl(string key, TimeSpan expiry)
    {
        return _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        });
    }

    public async Task<BlobMetadata?> GetObjectMetadataAsync(string key)
    {
        try
        {
            var response = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = key
            });
            return new BlobMetadata
            {
                ContentLength = response.ContentLength,
                ContentType = response.Headers.ContentType ?? string.Empty
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(string key)
    {
        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        });
    }
}
