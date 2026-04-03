using System.Globalization;
using SyncServer.Content.Contracts;
using SyncServer.Content.Models;

namespace SyncServer.Content.Plugins;

/// <summary>
/// Manga content plugin.
/// Supported formats: CBZ, CBR, PDF, EPUB, ZIP
/// Metadata: mangaFormat, author, artist, genre, chapters, volumes, status,
///           demographic, publisher, language, releaseDate, synopsis,
///           originalTitle, translationGroup, series, volume
/// Object path: manga/{author}/{series}/{volume}/{title}.{ext}
/// </summary>
public class MangaPlugin : IContentPlugin
{
    public string ContentType => "manga";
    public string DisplayName => "Manga";
    public string TableName => "Manga";
    public string StoragePrefix => "manga/";

    public IReadOnlySet<string> AllowedMimeTypes { get; } = new HashSet<string>
    {
        "application/x-cbz",
        "application/x-cbr",
        "application/pdf",
        "application/epub+zip",
        "application/zip",
        "application/octet-stream"
    };

    public string? ValidateMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return "metadata is required.";
        if (!metadata.TryGetValue("author", out var author) || string.IsNullOrWhiteSpace(author))
            return "author is required.";
        if (!metadata.TryGetValue("mangaFormat", out var fmt) || string.IsNullOrWhiteSpace(fmt))
            return "mangaFormat is required.";
        return null;
    }

    public string BuildStoragePath(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var author = m.GetValueOrDefault("author") ?? "Unknown";
        var series = m.GetValueOrDefault("series") ?? "general";
        var volume = m.GetValueOrDefault("volume") ?? "general";
        var title = m.GetValueOrDefault("title") ?? "Unknown";
        var ext = ResolveExtension(m.GetValueOrDefault("mangaFormat"));
        return $"manga/{Sanitize(author)}/{Sanitize(series)}/{Sanitize(volume)}/{Sanitize(title)}.{ext}";
    }

    public string ResolveMimeType(Dictionary<string, string>? metadata)
    {
        var fmt = (metadata ?? new Dictionary<string, string>()).GetValueOrDefault("mangaFormat") ?? "cbz";
        return fmt.ToLowerInvariant() switch
        {
            "cbz"  => "application/x-cbz",
            "cbr"  => "application/x-cbr",
            "pdf"  => "application/pdf",
            "epub" => "application/epub+zip",
            "zip"  => "application/zip",
            _ => "application/octet-stream"
        };
    }

    public Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams)
    {
        queryParams.TryGetValue("author", out var author);
        queryParams.TryGetValue("artist", out var artist);
        queryParams.TryGetValue("genre", out var genre);
        queryParams.TryGetValue("status", out var status);
        queryParams.TryGetValue("demographic", out var demographic);
        queryParams.TryGetValue("language", out var language);
        queryParams.TryGetValue("releaseFrom", out var releaseFromStr);
        queryParams.TryGetValue("releaseTo", out var releaseToStr);

        var releaseFrom = ParseDate(releaseFromStr);
        var releaseTo = ParseDate(releaseToStr);

        return item =>
        {
            var m = item.Metadata ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(author) && !(m.GetValueOrDefault("author") ?? "").Contains(author, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(artist) && !(m.GetValueOrDefault("artist") ?? "").Contains(artist, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(genre) && !string.Equals(m.GetValueOrDefault("genre"), genre, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(status) && !string.Equals(m.GetValueOrDefault("status"), status, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(demographic) && !string.Equals(m.GetValueOrDefault("demographic"), demographic, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(language) && !string.Equals(m.GetValueOrDefault("language"), language, StringComparison.OrdinalIgnoreCase))
                return false;
            if (releaseFrom != null || releaseTo != null)
            {
                var rd = m.GetValueOrDefault("releaseDate");
                if (string.IsNullOrEmpty(rd)) return false;
                if (!DateTimeOffset.TryParse(rd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return false;
                if (releaseFrom != null && d < releaseFrom) return false;
                if (releaseTo != null && d > releaseTo) return false;
            }
            return true;
        };
    }

    private static string ResolveExtension(string? fmt) =>
        (fmt ?? "cbz").ToLowerInvariant() switch
        {
            "cbz"  => "cbz",
            "cbr"  => "cbr",
            "pdf"  => "pdf",
            "epub" => "epub",
            "zip"  => "zip",
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
