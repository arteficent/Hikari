using System.Globalization;
using SyncServer.Content.Contracts;
using SyncServer.Content.Models;

namespace SyncServer.Content.Plugins;

/// <summary>
/// Audio content plugin.
/// Supported formats: MP3, WAV, FLAC, AIFF, AAC, OGG, M4A
/// Metadata: artist, album, genre, audioFormat, releaseDate, composer, lyricist,
///           trackNumber, albumArtist, bitrate, sampleRate, duration,
///           isrc, publisher, copyright, producer, label, language, explicitContent
/// </summary>
public class AudioPlugin : IContentPlugin
{
    public string ContentType => "audio";
    public string DisplayName => "Audio";
    public string TableName => "Audio";
    public string StoragePrefix => "audio/";

    public IReadOnlySet<string> AllowedMimeTypes { get; } = new HashSet<string>
    {
        "audio/mpeg",        // MP3
        "audio/wav",         // WAV
        "audio/x-wav",
        "audio/flac",        // FLAC
        "audio/aiff",        // AIFF
        "audio/x-aiff",
        "audio/aac",         // AAC
        "audio/mp4",         // M4A
        "audio/x-m4a",
        "audio/ogg",         // OGG Vorbis
        "application/octet-stream"
    };

    public string? ValidateMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return "metadata is required.";
        if (!metadata.TryGetValue("artist", out var artist) || string.IsNullOrWhiteSpace(artist))
            return "artist is required.";
        if (!metadata.TryGetValue("album", out var album) || string.IsNullOrWhiteSpace(album))
            return "album is required.";
        if (!metadata.TryGetValue("genre", out var genre) || string.IsNullOrWhiteSpace(genre))
            return "genre is required.";
        return null;
    }

    public string BuildStoragePath(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var artist = m.GetValueOrDefault("artist") ?? "Unknown";
        var album = m.GetValueOrDefault("album") ?? "Unknown";
        var title = m.GetValueOrDefault("title") ?? "Unknown";
        var ext = ResolveExtension(m.GetValueOrDefault("audioFormat"));
        return $"audio/{Sanitize(artist)}/{Sanitize(album)}/{Sanitize(title)}.{ext}";
    }

    public string ResolveMimeType(Dictionary<string, string>? metadata)
    {
        var fmt = (metadata ?? new Dictionary<string, string>()).GetValueOrDefault("audioFormat") ?? "mp3";
        return fmt.ToLowerInvariant() switch
        {
            "mp3"  => "audio/mpeg",
            "wav"  => "audio/wav",
            "flac" => "audio/flac",
            "aiff" => "audio/aiff",
            "aac"  => "audio/aac",
            "ogg"  => "audio/ogg",
            "m4a"  => "audio/mp4",
            _ => "application/octet-stream"
        };
    }

    public Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams)
    {
        queryParams.TryGetValue("genre", out var genre);
        queryParams.TryGetValue("album", out var album);
        queryParams.TryGetValue("artist", out var artist);
        queryParams.TryGetValue("composer", out var composer);
        queryParams.TryGetValue("playlist", out var playlist);
        queryParams.TryGetValue("releaseFrom", out var releaseFromStr);
        queryParams.TryGetValue("releaseTo", out var releaseToStr);

        var releaseFrom = ParseDate(releaseFromStr);
        var releaseTo = ParseDate(releaseToStr);

        return item =>
        {
            var m = item.Metadata ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(genre) && !string.Equals(m.GetValueOrDefault("genre"), genre, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(album) && !string.Equals(m.GetValueOrDefault("album"), album, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(artist) && !(m.GetValueOrDefault("artist") ?? "").Contains(artist, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(composer) && !(m.GetValueOrDefault("composer") ?? "").Contains(composer, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(playlist) && (item.Tags == null || !item.Tags.Contains(playlist)))
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

    private static string ResolveExtension(string? fmt)
    {
        return (fmt ?? "mp3").ToLowerInvariant() switch
        {
            "mp3"  => "mp3",
            "wav"  => "wav",
            "flac" => "flac",
            "aiff" => "aiff",
            "aac"  => "aac",
            "ogg"  => "ogg",
            "m4a"  => "m4a",
            _ => "bin"
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

    private static string Sanitize(string value) =>
        value.Replace("/", "-").Replace("\\", "-").Replace(" ", "-");
}
