using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Reads and rewrites embedded tags via TagLib#. Plays the combined role of the
/// android client's <c>AudioMetadataExtractor</c>, <c>AudioMetadataRewriter</c>,
/// <c>VideoMetadataRewriter</c> and <c>FileMetadataStripper</c>.
///
/// Every method is best-effort: an unsupported or corrupt container degrades to
/// "no metadata" / "leave the bytes alone" rather than failing the operation.
/// </summary>
public static class TagTool
{
    // ── Audio ────────────────────────────────────────────────

    public static Dictionary<string, string> ReadAudio(string filePath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;

            Put(result, "title", tag.Title);
            Put(result, "artist", tag.FirstPerformer ?? tag.FirstAlbumArtist);
            Put(result, "album", tag.Album);
            Put(result, "genre", tag.FirstGenre);
            Put(result, "composer", tag.FirstComposer);
            Put(result, "albumArtist", tag.FirstAlbumArtist);
            Put(result, "copyright", tag.Copyright);
            if (tag.Track > 0) result["trackNumber"] = tag.Track.ToString();
            if (tag.Year > 0) result["releaseDate"] = tag.Year.ToString();

            var properties = file.Properties;
            if (properties is not null)
            {
                if (properties.Duration > TimeSpan.Zero)
                {
                    result["duration"] = ((int)properties.Duration.TotalSeconds).ToString();
                }
                if (properties.AudioBitrate > 0) result["bitrate"] = properties.AudioBitrate.ToString();
                if (properties.AudioSampleRate > 0) result["sampleRate"] = properties.AudioSampleRate.ToString();
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ReadAudio({Path.GetFileName(filePath)}) failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Strip every existing tag and write a fresh set from the upload form, so the
    /// server never receives metadata the user didn't intend to publish.
    /// </summary>
    public static void WriteAudio(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath)
    {
        using var file = TagLib.File.Create(filePath);

        file.RemoveTags(TagLib.TagTypes.AllTags);
        var tag = file.Tag;

        tag.Title = title;
        tag.Performers = Split(Get(fields, "artist"));
        tag.AlbumArtists = Split(Get(fields, "albumArtist", Get(fields, "artist")));
        tag.Album = NullIfBlank(Get(fields, "album"));
        tag.Genres = Split(Get(fields, "genre"));
        tag.Composers = Split(Get(fields, "composer"));
        tag.Copyright = NullIfBlank(Get(fields, "copyright"));
        tag.Publisher = NullIfBlank(Get(fields, "publisher"));
        tag.Lyrics = NullIfBlank(Get(fields, "lyrics"));

        if (uint.TryParse(Get(fields, "trackNumber"), out var track)) tag.Track = track;
        if (TryParseYear(Get(fields, "releaseDate"), out var year)) tag.Year = year;

        ApplyCover(tag, coverImagePath);

        file.Save();
    }

    public static byte[]? ReadCoverArt(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var picture = file.Tag.Pictures.FirstOrDefault();
            return picture?.Data?.Data is { Length: > 0 } data ? data : null;
        }
        catch
        {
            return null;
        }
    }

    // ── Video ────────────────────────────────────────────────

    public static Dictionary<string, string> ReadVideo(string filePath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var file = TagLib.File.Create(filePath);

            Put(result, "title", file.Tag.Title);
            Put(result, "genre", file.Tag.FirstGenre);

            var properties = file.Properties;
            if (properties is not null)
            {
                if (properties.Duration > TimeSpan.Zero)
                {
                    result["duration"] = ((int)properties.Duration.TotalSeconds).ToString();
                }
                if (properties.VideoWidth > 0 && properties.VideoHeight > 0)
                {
                    result["resolution"] = $"{properties.VideoWidth}x{properties.VideoHeight}";
                }
                if (properties.Description is { Length: > 0 }) result["codec"] = properties.Description;
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ReadVideo({Path.GetFileName(filePath)}) failed: {ex.Message}");
        }

        return result;
    }

    public static void WriteVideo(string filePath, string title, IReadOnlyDictionary<string, string> fields)
    {
        using var file = TagLib.File.Create(filePath);

        file.RemoveTags(TagLib.TagTypes.AllTags);
        var tag = file.Tag;

        tag.Title = title;
        tag.Genres = Split(Get(fields, "genre"));
        tag.Performers = Split(Get(fields, "director"));
        if (TryParseYear(Get(fields, "releaseDate"), out var year)) tag.Year = year;

        file.Save();
    }

    // ── Images ───────────────────────────────────────────────

    public static Dictionary<string, string> ReadImage(string filePath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var file = TagLib.File.Create(filePath);

            var properties = file.Properties;
            if (properties is not null)
            {
                if (properties.PhotoWidth > 0) result["width"] = properties.PhotoWidth.ToString();
                if (properties.PhotoHeight > 0) result["height"] = properties.PhotoHeight.ToString();
            }

            if (file is TagLib.Image.File imageFile)
            {
                var tag = imageFile.ImageTag;
                Put(result, "title", tag.Title);
                Put(result, "creator", tag.Creator);
                Put(result, "copyright", tag.Copyright);
                Put(result, "cameraMake", tag.Make);
                Put(result, "cameraModel", tag.Model);
                if (tag.Keywords is { Length: > 0 }) result["keywords"] = string.Join(", ", tag.Keywords);
                if (tag.DateTime is { } taken) result["dateTaken"] = taken.ToString("yyyy-MM-dd");
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ReadImage({Path.GetFileName(filePath)}) failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Strip EXIF/XMP/IPTC (which routinely carries GPS coordinates and device
    /// serial numbers) and write back only the fields the user typed.
    /// </summary>
    public static void WriteImage(string filePath, string title, IReadOnlyDictionary<string, string> fields)
    {
        using var file = TagLib.File.Create(filePath);

        file.RemoveTags(TagLib.TagTypes.AllTags);

        if (file is TagLib.Image.File imageFile)
        {
            var tag = imageFile.ImageTag;
            tag.Title = title;
            tag.Creator = NullIfBlank(Get(fields, "creator"));
            tag.Copyright = NullIfBlank(Get(fields, "copyright"));

            var keywords = Split(Get(fields, "keywords"));
            if (keywords.Length > 0) tag.Keywords = keywords;

            if (DateTime.TryParse(Get(fields, "dateTaken"), out var taken)) tag.DateTime = taken;
        }

        file.Save();
    }

    // ── Shared helpers ───────────────────────────────────────

    private static void ApplyCover(TagLib.Tag tag, string? coverImagePath)
    {
        if (string.IsNullOrWhiteSpace(coverImagePath) || !File.Exists(coverImagePath))
        {
            tag.Pictures = Array.Empty<TagLib.IPicture>();
            return;
        }

        try
        {
            var picture = new TagLib.Picture(coverImagePath)
            {
                Type = TagLib.PictureType.FrontCover,
                Description = "Cover",
            };
            tag.Pictures = [picture];
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not embed cover image: {ex.Message}");
            tag.Pictures = Array.Empty<TagLib.IPicture>();
        }
    }

    private static void Put(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target[key] = value.Trim();
    }

    private static string Get(IReadOnlyDictionary<string, string> fields, string key, string fallback = "") =>
        fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string[] Split(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseYear(string value, out uint year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (DateTime.TryParse(value, out var parsed))
        {
            year = (uint)parsed.Year;
            return true;
        }

        return uint.TryParse(value.AsSpan(0, Math.Min(4, value.Length)), out year) && year > 0;
    }
}
