using SyncServer.Content.Models;

namespace SyncServer.Content.Contracts;

/// <summary>
/// Contract that every content-type plugin must implement.
/// A plugin defines how a specific content type (music, book, manga, etc.)
/// stores, validates, and filters its data.
/// </summary>
public interface IContentPlugin
{
    /// <summary>
    /// Unique content type key, e.g. "music", "book", "manga".
    /// Used in API routes: /content/{contentType}/...
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Human-readable display name, e.g. "Music", "Books".
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// DynamoDB table name for this content type's items.
    /// </summary>
    string TableName { get; }

    /// <summary>
    /// S3 key prefix for stored binaries, e.g. "music/song/", "books/".
    /// </summary>
    string StoragePrefix { get; }

    /// <summary>
    /// MIME types this plugin accepts for upload.
    /// </summary>
    IReadOnlySet<string> AllowedMimeTypes { get; }

    /// <summary>
    /// Validate plugin-specific metadata before upload.
    /// Returns null if valid, or an error message string if invalid.
    /// </summary>
    string? ValidateMetadata(Dictionary<string, string>? metadata);

    /// <summary>
    /// Derive the S3 storage path from item metadata.
    /// </summary>
    string BuildStoragePath(Dictionary<string, string>? metadata);

    /// <summary>
    /// Resolve the MIME type string for the given metadata.
    /// Falls back to "application/octet-stream".
    /// </summary>
    string ResolveMimeType(Dictionary<string, string>? metadata);

    /// <summary>
    /// Build an in-memory filter predicate from the query params supplied by the client.
    /// Only plugin-specific params (genre, author, etc.) are handled here;
    /// generic params (page, pageSize, lastModifiedSince, titlePrefix) are handled by infra.
    /// </summary>
    Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams);

    /// <summary>
    /// Optional hook executed after a successful upload (e.g. thumbnail generation).
    /// Default does nothing.
    /// </summary>
    Task OnAfterUploadAsync(ContentItem item, Stream binary) => Task.CompletedTask;

    /// <summary>
    /// Optional hook executed before deletion (e.g. cleanup related resources).
    /// Default does nothing.
    /// </summary>
    Task OnBeforeDeleteAsync(ContentItem item) => Task.CompletedTask;
}
