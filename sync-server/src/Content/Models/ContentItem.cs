using System.ComponentModel.DataAnnotations;
using Amazon.DynamoDBv2.DataModel;

namespace SyncServer.Content.Models;

/// <summary>
/// Generic content item stored in DynamoDB.
/// Plugin-specific fields live in the Metadata dictionary.
/// Every content type shares this schema — the plugin interprets the Metadata bag.
/// </summary>
[DynamoDBTable("ContentItems")]   // default; overridden per-plugin via DynamoDBOperationConfig
public class ContentItem
{
    [DynamoDBHashKey]
    public Guid Id { get; set; }

    /// <summary>
    /// The plugin key that owns this item, e.g. "music", "book".
    /// Also used as a GSI hash key for efficient per-type queries.
    /// </summary>
    [DynamoDBGlobalSecondaryIndexHashKey("contentType-index")]
    [DynamoDBProperty]
    public string ContentType { get; set; } = string.Empty;

    [Required]
    [DynamoDBProperty]
    public string? Title { get; set; }

    [DynamoDBProperty]
    public string? Description { get; set; }

    /// <summary>
    /// MIME type string, e.g. "audio/flac", "application/pdf".
    /// </summary>
    [Required]
    [DynamoDBProperty]
    public string? Format { get; set; }

    [Required]
    [DynamoDBProperty]
    public long SizeInBytes { get; set; }

    [DynamoDBProperty]
    public string? StoragePath { get; set; }

    [DynamoDBProperty]
    public DateTime? LastModified { get; set; }

    [DynamoDBProperty]
    public DateTime CreatedAt { get; set; }

    [DynamoDBProperty]
    public string[]? Tags { get; set; }

    /// <summary>
    /// Plugin-specific metadata stored as a flat string→string dictionary.
    /// Example for music: { "artist": "Nightfall", "album": "Dream", "genre": "Rock", ... }
    /// Example for book:  { "author": "Tolkien", "isbn": "978-...", "pages": "300" }
    /// </summary>
    [DynamoDBProperty]
    public Dictionary<string, string>? Metadata { get; set; }
}
