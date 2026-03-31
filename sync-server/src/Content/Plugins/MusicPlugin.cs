using System.Globalization;
using SyncServer.Content.Contracts;
using SyncServer.Content.Models;

namespace SyncServer.Content.Plugins;

/// <summary>
/// Music content plugin — maps the original Music entity schema onto the generic ContentItem model.
/// Metadata keys: artist, album, genre, releaseDate, duration, bitrate, musicFormat,
///                lyrics, publisher, copyright, language, countryOfOrigin, isrc, producer, label, explicitContent
/// </summary>
public class MusicPlugin : IContentPlugin
{
    public string ContentType => "music";
    public string DisplayName => "Music";
    public string TableName => "Music";
    public string StoragePrefix => "music/song/";

    public IReadOnlySet<string> AllowedMimeTypes { get; } = new HashSet<string>
    {
        "audio/mpeg",
        "audio/wav",
        "audio/flac",
        "application/octet-stream"
    };

    public string? ValidateMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return "metadata is required.";
        if (!metadata.ContainsKey("artist") || string.IsNullOrEmpty(metadata["artist"]))
            return "artist is required.";
        if (!metadata.ContainsKey("album") || string.IsNullOrEmpty(metadata["album"]))
            return "album is required.";
        if (!metadata.ContainsKey("genre") || string.IsNullOrEmpty(metadata["genre"]))
            return "genre is required.";
        return null; // valid
    }

    public string BuildStoragePath(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var artist = m.GetValueOrDefault("artist") ?? "Unknown";
        var album = m.GetValueOrDefault("album") ?? "Unknown";
        var title = m.GetValueOrDefault("title") ?? "Unknown";
        var format = m.GetValueOrDefault("musicFormat") ?? "mp3";

        // Map format enum-like values to extension
        var ext = format switch
        {
            "1" or "AudioMpeg" => "mp3",
            "2" or "AudioWav" => "wav",
            "3" or "AudioFlac" => "flac",
            _ => format.ToLowerInvariant()
        };

        return $"music/song/{Sanitize(artist)}/{Sanitize(album)}/{Sanitize(title)}.{ext}";
    }

    public string ResolveMimeType(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var format = m.GetValueOrDefault("musicFormat") ?? "1";
        return format switch
        {
            "1" or "AudioMpeg" => "audio/mpeg",
            "2" or "AudioWav" => "audio/wav",
            "3" or "AudioFlac" => "audio/flac",
            _ => "application/octet-stream"
        };
    }

    public Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams)
    {
        queryParams.TryGetValue("genre", out var genre);
        queryParams.TryGetValue("album", out var album);
        queryParams.TryGetValue("artist", out var artist);
        queryParams.TryGetValue("playlist", out var playlist);
        queryParams.TryGetValue("releaseFrom", out var releaseFromStr);
        queryParams.TryGetValue("releaseTo", out var releaseToStr);

        var releaseFrom = ParseDate(releaseFromStr);
        var releaseTo = ParseDate(releaseToStr);

        return item =>
        {
            var m = item.Metadata ?? new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(genre) && m.GetValueOrDefault("genre") != genre)
                return false;
            if (!string.IsNullOrEmpty(album) && m.GetValueOrDefault("album") != album)
                return false;
            if (!string.IsNullOrEmpty(artist))
            {
                var a = m.GetValueOrDefault("artist") ?? "";
                if (!a.Contains(artist, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (!string.IsNullOrEmpty(playlist))
            {
                if (item.Tags == null || !item.Tags.Contains(playlist))
                    return false;
            }
            if (releaseFrom != null || releaseTo != null)
            {
                var releaseDateStr = m.GetValueOrDefault("releaseDate");
                if (string.IsNullOrEmpty(releaseDateStr)) return false;
                if (!DateTimeOffset.TryParse(releaseDateStr, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var releaseDate))
                    return false;
                if (releaseFrom != null && releaseDate < releaseFrom) return false;
                if (releaseTo != null && releaseDate > releaseTo) return false;
            }
            return true;
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return null;
    }

    private static string Sanitize(string value)
    {
        return value.Replace("/", "-").Replace("\\", "-").Replace(" ", "-");
    }
}
