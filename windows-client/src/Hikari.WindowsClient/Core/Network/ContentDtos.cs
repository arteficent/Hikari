using System.Text.Json.Serialization;

namespace Hikari.WindowsClient.Core.Network;

/// <summary>
/// Generic content item matching the server's ContentItem entity.
/// Plugin-specific fields live in <see cref="Metadata"/>.
/// Mirrors <c>android-client/app/src/core/network/ContentDtos.kt</c>.
/// </summary>
public sealed class ContentItem
{
    public string Id { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Format { get; set; }
    public long SizeInBytes { get; set; }
    public string? StoragePath { get; set; }
    public string? LastModified { get; set; }
    public string? CreatedAt { get; set; }
    public List<string>? Tags { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    public string Meta(string key) =>
        Metadata is not null && Metadata.TryGetValue(key, out var v) ? v : string.Empty;

    public ContentItem Clone() => new()
    {
        Id = Id,
        ContentType = ContentType,
        Title = Title,
        Description = Description,
        Format = Format,
        SizeInBytes = SizeInBytes,
        StoragePath = StoragePath,
        LastModified = LastModified,
        CreatedAt = CreatedAt,
        Tags = Tags is null ? null : new List<string>(Tags),
        Metadata = Metadata is null ? null : new Dictionary<string, string>(Metadata),
    };
}

/// <summary>
/// Download descriptor for any content type. The server returns a presigned URL
/// rather than an inline base64 payload.
/// </summary>
public sealed class ContentDownloadResponse
{
    public ContentItem? Item { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ExpiresAtUtc { get; set; }
}

/// <summary>Info about a registered server plugin.</summary>
public sealed class PluginInfo
{
    public string ContentType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> AllowedMimeTypes { get; set; } = new();
}

public sealed class ContentUploadInitRequest
{
    public ContentItem Item { get; set; } = new();
    public int UrlExpiresInMinutes { get; set; } = 15;
}

public sealed class ContentUploadInitResponse
{
    public ContentItem Item { get; set; } = new();
    public string UploadUrl { get; set; } = string.Empty;
    public string? ExpiresAtUtc { get; set; }
    public Dictionary<string, string> RequiredHeaders { get; set; } = new();
}

public sealed class ContentUploadCompleteRequest
{
    public ContentItem Item { get; set; } = new();
}

public sealed class ContentUploadCompleteResponse
{
    public string Message { get; set; } = string.Empty;
    public ContentItem? Item { get; set; }
}

public sealed class ContentDeleteRequest
{
    public List<ContentItem> Items { get; set; } = new();
}

/// <summary>
/// The server serialises these two properties in PascalCase; deserialisation is
/// configured case-insensitively so both casings round-trip.
/// </summary>
public sealed class ContentDeleteResponse
{
    [JsonPropertyName("deleted")]
    public List<string> Deleted { get; set; } = new();

    [JsonPropertyName("failed")]
    public List<string> Failed { get; set; } = new();
}
