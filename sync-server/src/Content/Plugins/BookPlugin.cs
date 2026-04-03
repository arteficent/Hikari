using System.Globalization;
using SyncServer.Content.Contracts;
using SyncServer.Content.Models;

namespace SyncServer.Content.Plugins;

/// <summary>
/// Book content plugin.
/// Supported formats: EPUB, PDF, MOBI, AZW3, TXT, RTF, DOCX, HTML
/// Metadata: bookFormat, author, isbn, publisher, pages, language, genre,
///           series, volume, publicationDate, synopsis, coverImageUrl
/// Object path: book/{author}/{series}/{volume}/{title}.{ext}
/// </summary>
public class BookPlugin : IContentPlugin
{
    public string ContentType => "book";
    public string DisplayName => "Book";
    public string TableName => "Book";
    public string StoragePrefix => "book/";

    public IReadOnlySet<string> AllowedMimeTypes { get; } = new HashSet<string>
    {
        "application/epub+zip",
        "application/pdf",
        "application/x-mobipocket-ebook",
        "application/vnd.amazon.ebook",
        "text/plain",
        "application/rtf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/html",
        "application/octet-stream"
    };

    public string? ValidateMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return "metadata is required.";
        if (!metadata.TryGetValue("author", out var author) || string.IsNullOrWhiteSpace(author))
            return "author is required.";
        if (!metadata.TryGetValue("bookFormat", out var fmt) || string.IsNullOrWhiteSpace(fmt))
            return "bookFormat is required.";
        return null;
    }

    public string BuildStoragePath(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var author = m.GetValueOrDefault("author") ?? "Unknown";
        var series = m.GetValueOrDefault("series") ?? "general";
        var volume = m.GetValueOrDefault("volume") ?? "general";
        var title = m.GetValueOrDefault("title") ?? "Unknown";
        var ext = ResolveExtension(m.GetValueOrDefault("bookFormat"));
        return $"book/{Sanitize(author)}/{Sanitize(series)}/{Sanitize(volume)}/{Sanitize(title)}.{ext}";
    }

    public string ResolveMimeType(Dictionary<string, string>? metadata)
    {
        var fmt = (metadata ?? new Dictionary<string, string>()).GetValueOrDefault("bookFormat") ?? "epub";
        return fmt.ToLowerInvariant() switch
        {
            "epub" => "application/epub+zip",
            "pdf"  => "application/pdf",
            "mobi" => "application/x-mobipocket-ebook",
            "azw3" => "application/vnd.amazon.ebook",
            "txt"  => "text/plain",
            "rtf"  => "application/rtf",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "html" => "text/html",
            _ => "application/octet-stream"
        };
    }

    public Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams)
    {
        queryParams.TryGetValue("author", out var author);
        queryParams.TryGetValue("genre", out var genre);
        queryParams.TryGetValue("publisher", out var publisher);
        queryParams.TryGetValue("language", out var language);
        queryParams.TryGetValue("series", out var series);
        queryParams.TryGetValue("releaseFrom", out var releaseFromStr);
        queryParams.TryGetValue("releaseTo", out var releaseToStr);

        var releaseFrom = ParseDate(releaseFromStr);
        var releaseTo = ParseDate(releaseToStr);

        return item =>
        {
            var m = item.Metadata ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(author) && !(m.GetValueOrDefault("author") ?? "").Contains(author, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(genre) && !string.Equals(m.GetValueOrDefault("genre"), genre, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(publisher) && !(m.GetValueOrDefault("publisher") ?? "").Contains(publisher, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(language) && !string.Equals(m.GetValueOrDefault("language"), language, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(series) && !(m.GetValueOrDefault("series") ?? "").Contains(series, StringComparison.OrdinalIgnoreCase))
                return false;
            if (releaseFrom != null || releaseTo != null)
            {
                var rd = m.GetValueOrDefault("publicationDate");
                if (string.IsNullOrEmpty(rd)) return false;
                if (!DateTimeOffset.TryParse(rd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return false;
                if (releaseFrom != null && d < releaseFrom) return false;
                if (releaseTo != null && d > releaseTo) return false;
            }
            return true;
        };
    }

    private static string ResolveExtension(string? fmt) =>
        (fmt ?? "epub").ToLowerInvariant() switch
        {
            "epub" => "epub",
            "pdf"  => "pdf",
            "mobi" => "mobi",
            "azw3" => "azw3",
            "txt"  => "txt",
            "rtf"  => "rtf",
            "docx" => "docx",
            "html" => "html",
            _ => "bin"
        };

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed);
        return parsed == default ? null : parsed;
    }

    private static string Sanitize(string value) =>
        value.Replace("/", "-").Replace("\\", "-").Replace(" ", "-");
}
