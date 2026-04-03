using System.Globalization;
using SyncServer.Content.Contracts;
using SyncServer.Content.Models;

namespace SyncServer.Content.Plugins;

/// <summary>
/// Video content plugin.
/// Supported formats: MP4, MOV, AVI, MKV, WMV, WebM, FLV
/// Metadata: videoFormat, type (animation|live), codec, resolution, fps, bitrate, duration,
///           director, producer, genre, releaseDate, language, subtitleLanguages,
///           season, episode, series, studio, rating, country
/// Object path: video/{type}/{series}/{season}/{episode}/{title}.{ext}
/// </summary>
public class VideoPlugin : IContentPlugin
{
    public string ContentType => "video";
    public string DisplayName => "Video";
    public string TableName => "Video";
    public string StoragePrefix => "video/";

    public IReadOnlySet<string> AllowedMimeTypes { get; } = new HashSet<string>
    {
        "video/mp4",
        "video/quicktime",      // MOV
        "video/x-msvideo",      // AVI
        "video/x-matroska",     // MKV
        "video/x-ms-wmv",       // WMV
        "video/webm",
        "video/x-flv",
        "application/octet-stream"
    };

    public string? ValidateMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return "metadata is required.";
        if (!metadata.TryGetValue("videoFormat", out var fmt) || string.IsNullOrWhiteSpace(fmt))
            return "videoFormat is required.";
        if (metadata.TryGetValue("type", out var type) && !string.IsNullOrWhiteSpace(type))
        {
            var lower = type.ToLowerInvariant();
            if (lower != "animation" && lower != "live")
                return "type must be 'animation' or 'live'.";
        }
        return null;
    }

    public string BuildStoragePath(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var type = m.GetValueOrDefault("type") ?? "general";
        var series = m.GetValueOrDefault("series") ?? "general";
        var season = m.GetValueOrDefault("season") ?? "general";
        var episode = m.GetValueOrDefault("episode") ?? "general";
        var title = m.GetValueOrDefault("title") ?? "Unknown";
        var ext = ResolveExtension(m.GetValueOrDefault("videoFormat"));
        return $"video/{Sanitize(type)}/{Sanitize(series)}/{Sanitize(season)}/{Sanitize(episode)}/{Sanitize(title)}.{ext}";
    }

    public string ResolveMimeType(Dictionary<string, string>? metadata)
    {
        var fmt = (metadata ?? new Dictionary<string, string>()).GetValueOrDefault("videoFormat") ?? "mp4";
        return fmt.ToLowerInvariant() switch
        {
            "mp4"  => "video/mp4",
            "mov"  => "video/quicktime",
            "avi"  => "video/x-msvideo",
            "mkv"  => "video/x-matroska",
            "wmv"  => "video/x-ms-wmv",
            "webm" => "video/webm",
            "flv"  => "video/x-flv",
            _ => "application/octet-stream"
        };
    }

    public Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams)
    {
        queryParams.TryGetValue("genre", out var genre);
        queryParams.TryGetValue("director", out var director);
        queryParams.TryGetValue("series", out var series);
        queryParams.TryGetValue("resolution", out var resolution);
        queryParams.TryGetValue("codec", out var codec);
        queryParams.TryGetValue("type", out var type);
        queryParams.TryGetValue("season", out var season);
        queryParams.TryGetValue("episode", out var episode);
        queryParams.TryGetValue("releaseFrom", out var releaseFromStr);
        queryParams.TryGetValue("releaseTo", out var releaseToStr);

        var releaseFrom = ParseDate(releaseFromStr);
        var releaseTo = ParseDate(releaseToStr);

        return item =>
        {
            var m = item.Metadata ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(genre) && !string.Equals(m.GetValueOrDefault("genre"), genre, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(director) && !(m.GetValueOrDefault("director") ?? "").Contains(director, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(series) && !(m.GetValueOrDefault("series") ?? "").Contains(series, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(resolution) && !string.Equals(m.GetValueOrDefault("resolution"), resolution, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(codec) && !string.Equals(m.GetValueOrDefault("codec"), codec, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(type) && !string.Equals(m.GetValueOrDefault("type"), type, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(season) && !string.Equals(m.GetValueOrDefault("season"), season, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(episode) && !string.Equals(m.GetValueOrDefault("episode"), episode, StringComparison.OrdinalIgnoreCase))
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
        (fmt ?? "mp4").ToLowerInvariant() switch
        {
            "mp4"  => "mp4",
            "mov"  => "mov",
            "avi"  => "avi",
            "mkv"  => "mkv",
            "wmv"  => "wmv",
            "webm" => "webm",
            "flv"  => "flv",
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
