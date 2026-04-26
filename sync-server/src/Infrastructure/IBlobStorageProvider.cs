namespace SyncServer.Infrastructure;

/// <summary>
/// Cloud-agnostic blob/object storage abstraction.
/// Implementations: S3, R2, Azure Blob, GCS, etc.
/// </summary>
public interface IBlobStorageProvider
{
    /// <summary>Generate a presigned URL the client can PUT a file to.</summary>
    string GenerateUploadUrl(string key, string contentType, TimeSpan expiry);

    /// <summary>Generate a presigned URL the client can GET a file from.</summary>
    string GenerateDownloadUrl(string key, TimeSpan expiry);

    /// <summary>Retrieve size and content-type metadata for an object. Returns null if not found.</summary>
    Task<BlobMetadata?> GetObjectMetadataAsync(string key);

    /// <summary>Delete a single object by key.</summary>
    Task DeleteObjectAsync(string key);
}

/// <summary>
/// Lightweight metadata returned by HEAD-style operations.
/// </summary>
public class BlobMetadata
{
    public long ContentLength { get; set; }
    public string ContentType { get; set; } = string.Empty;
}
