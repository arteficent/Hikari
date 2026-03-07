using SyncServer.Content.Models;

namespace SyncServer.Content.Dtos;

/// <summary>
/// Request to initialize a direct-to-storage upload using a presigned URL.
/// </summary>
public class ContentUploadInitRequest
{
    public ContentItem? Item { get; set; }
    public int UrlExpiresInMinutes { get; set; } = 15;
}

/// <summary>
/// Response for direct upload initialization.
/// Client uploads binary directly to S3 using UploadUrl and required headers.
/// </summary>
public class ContentUploadInitResponse
{
    public ContentItem? Item { get; set; }
    public string UploadUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Dictionary<string, string> RequiredHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Request to finalize metadata after direct upload succeeds.
/// </summary>
public class ContentUploadCompleteRequest
{
    public ContentItem? Item { get; set; }
}

/// <summary>
/// Generic delete request — works for any content type.
/// </summary>
public class ContentDeleteRequest
{
    public List<ContentItem> Items { get; set; } = new();
}

/// <summary>
/// Download response containing a direct presigned URL.
/// </summary>
public class ContentDownloadUrlResponse
{
    public ContentItem? Item { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
