using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content;

/// <summary>
/// Contract for a client-side content plugin. Each content type (audio, video,
/// book, manga, image) implements this to define how items are stored locally,
/// displayed, filtered and uploaded.
///
/// Mirrors <c>android-client/app/src/content/ContentPlugin.kt</c>. The two
/// deliberate differences are that (a) UI is declared via <see cref="FormField"/>
/// rather than emitted as Compose functions, and (b) binaries move as
/// <see cref="Stream"/>s rather than <c>ByteArray</c>, so multi-gigabyte video
/// never has to fit in memory.
/// </summary>
public interface IContentPlugin
{
    /// <summary>Server-side content type key ("audio", "book", …).</summary>
    string ContentType { get; }

    /// <summary>Human-readable name shown in the picker.</summary>
    string DisplayName { get; }

    /// <summary>Top-level folder under the library root ("audio", "book", …).</summary>
    string LocalDirectory { get; }

    /// <summary>Segoe Fluent Icons glyph used on the picker card.</summary>
    string Glyph { get; }

    /// <summary>One-line description shown on the picker card.</summary>
    string Tagline { get; }

    IReadOnlySet<string> SupportedMimeTypes { get; }

    // ── Local storage ────────────────────────────────────────

    /// <summary>
    /// Write <paramref name="content"/> into the library. Returns the
    /// library-relative path (forward-slashed) the item now occupies.
    /// </summary>
    Task<string> SaveLocallyAsync(string libraryRoot, ContentItem item, Stream content, CancellationToken ct = default);

    /// <summary>Delete a previously synced file by its library-relative path.</summary>
    bool DeleteLocally(string libraryRoot, string relativePath);

    /// <summary>Every file this plugin owns, as library-relative forward-slashed paths.</summary>
    IReadOnlyList<string> GetLocalItems(string libraryRoot);

    /// <summary>
    /// The library-relative path an item maps to. This is the android client's
    /// <c>displayName(item)</c> and is what gets recorded in the sync index.
    /// </summary>
    string RelativePathFor(ContentItem item);

    string MimeTypeFor(ContentItem item);

    /// <summary>Absolute path of the synced file, or null when it isn't on disk.</summary>
    string? GetLocalFile(string libraryRoot, ContentItem item);

    // ── Browse / filter UI ───────────────────────────────────

    /// <summary>Server-side filter inputs sent as query parameters.</summary>
    IReadOnlyList<FormField> FilterFields { get; }

    /// <summary>
    /// Metadata keys searchable via the client-side regex filter, as
    /// key → human-readable label. Drives the regex help tooltip.
    /// </summary>
    IReadOnlyDictionary<string, string> FilterableFields { get; }

    /// <summary>Secondary text under the title in the item list.</summary>
    string SecondaryLine(ContentItem item);

    // ── Upload ───────────────────────────────────────────────

    /// <summary>File extensions offered by the upload file picker, e.g. ".mp3".</summary>
    IReadOnlyList<string> UploadFileExtensions { get; }

    /// <summary>Whether this plugin supports a cover/thumbnail image alongside the main file.</summary>
    bool SupportsCoverImage { get; }

    /// <summary>Label for the cover image picker button (e.g. "Album Art").</summary>
    string CoverImageLabel { get; }

    IReadOnlyList<FormField> UploadFields { get; }

    /// <summary>Returns an error message, or null when the fields are valid.</summary>
    string? ValidateUploadFields(IReadOnlyDictionary<string, string> fields);

    Dictionary<string, string> BuildUploadMetadata(string title, IReadOnlyDictionary<string, string> fields);

    string ResolveUploadMimeType(IReadOnlyDictionary<string, string> fields);

    /// <summary>
    /// Value for <see cref="ContentItem.Format"/>. The server marks it
    /// <c>[Required]</c>, so upload-init returns 400 without it.
    /// </summary>
    string ResolveUploadFormat(IReadOnlyDictionary<string, string> fields, string sourceFilePath);

    /// <summary>
    /// Read metadata out of a picked file to pre-fill the upload form. Also
    /// populates the generic "title" when one is found.
    /// </summary>
    Dictionary<string, string> ExtractFileMetadata(string filePath);

    /// <summary>
    /// Produce the bytes to upload: strip the file's existing tags and write fresh
    /// ones from the form. The returned stream is positioned at 0 and owns any
    /// temporary file it is backed by, so disposing it cleans up.
    /// </summary>
    Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default);

    /// <summary>Embedded cover art of a locally synced item, or null.</summary>
    byte[]? ExtractCoverArt(string libraryRoot, ContentItem item);

    /// <summary>
    /// Embedded cover art of a file the user just picked for upload, so the
    /// existing artwork can be previewed before it is re-embedded.
    /// </summary>
    byte[]? ExtractCoverArtFromFile(string filePath);
}
