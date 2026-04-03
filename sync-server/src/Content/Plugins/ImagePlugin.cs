using System.Globalization;
using SyncServer.Content.Contracts;
using SyncServer.Content.Models;

namespace SyncServer.Content.Plugins;

/// <summary>
/// Image content plugin.
/// Supported formats: JPEG, PNG, WebP, GIF, SVG, TIFF, AVIF, HEIF, BMP, RAW
/// Metadata: imageFormat, creator, collection, copyright, keywords, cameraMake, cameraModel,
///           lens, aperture, shutterSpeed, iso, focalLength, gpsLocation,
///           width, height, colorSpace
/// Object path: image/{creator}/{collection}/{title}.{ext}
/// </summary>
public class ImagePlugin : IContentPlugin
{
    public string ContentType => "image";
    public string DisplayName => "Image";
    public string TableName => "Image";
    public string StoragePrefix => "image/";

    public IReadOnlySet<string> AllowedMimeTypes { get; } = new HashSet<string>
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/svg+xml",
        "image/tiff",
        "image/avif",
        "image/heif",
        "image/heic",
        "image/bmp",
        "image/x-raw",
        "application/octet-stream"
    };

    public string? ValidateMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return "metadata is required.";
        if (!metadata.TryGetValue("imageFormat", out var fmt) || string.IsNullOrWhiteSpace(fmt))
            return "imageFormat is required.";
        return null;
    }

    public string BuildStoragePath(Dictionary<string, string>? metadata)
    {
        var m = metadata ?? new Dictionary<string, string>();
        var creator = m.GetValueOrDefault("creator") ?? "general";
        var collection = m.GetValueOrDefault("collection") ?? "general";
        var title = m.GetValueOrDefault("title") ?? "Unknown";
        var ext = ResolveExtension(m.GetValueOrDefault("imageFormat"));
        return $"image/{Sanitize(creator)}/{Sanitize(collection)}/{Sanitize(title)}.{ext}";
    }

    public string ResolveMimeType(Dictionary<string, string>? metadata)
    {
        var fmt = (metadata ?? new Dictionary<string, string>()).GetValueOrDefault("imageFormat") ?? "jpeg";
        return fmt.ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => "image/jpeg",
            "png"  => "image/png",
            "webp" => "image/webp",
            "gif"  => "image/gif",
            "svg"  => "image/svg+xml",
            "tiff" => "image/tiff",
            "avif" => "image/avif",
            "heif" or "heic" => "image/heif",
            "bmp"  => "image/bmp",
            "raw"  => "image/x-raw",
            _ => "application/octet-stream"
        };
    }

    public Func<ContentItem, bool> BuildFilter(IDictionary<string, string?> queryParams)
    {
        queryParams.TryGetValue("creator", out var creator);
        queryParams.TryGetValue("collection", out var collection);
        queryParams.TryGetValue("keywords", out var keywords);
        queryParams.TryGetValue("cameraMake", out var cameraMake);
        queryParams.TryGetValue("cameraModel", out var cameraModel);
        queryParams.TryGetValue("dateFrom", out var dateFromStr);
        queryParams.TryGetValue("dateTo", out var dateToStr);

        var dateFrom = ParseDate(dateFromStr);
        var dateTo = ParseDate(dateToStr);

        return item =>
        {
            var m = item.Metadata ?? new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(creator) && !(m.GetValueOrDefault("creator") ?? "").Contains(creator, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(collection) && !(m.GetValueOrDefault("collection") ?? "").Contains(collection, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(keywords))
            {
                var kw = m.GetValueOrDefault("keywords") ?? "";
                if (!kw.Contains(keywords, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (!string.IsNullOrEmpty(cameraMake) && !string.Equals(m.GetValueOrDefault("cameraMake"), cameraMake, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(cameraModel) && !(m.GetValueOrDefault("cameraModel") ?? "").Contains(cameraModel, StringComparison.OrdinalIgnoreCase))
                return false;
            if (dateFrom != null || dateTo != null)
            {
                var rd = m.GetValueOrDefault("dateTaken");
                if (string.IsNullOrEmpty(rd)) return false;
                if (!DateTimeOffset.TryParse(rd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return false;
                if (dateFrom != null && d < dateFrom) return false;
                if (dateTo != null && d > dateTo) return false;
            }
            return true;
        };
    }

    private static string ResolveExtension(string? fmt) =>
        (fmt ?? "jpeg").ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => "jpg",
            "png"  => "png",
            "webp" => "webp",
            "gif"  => "gif",
            "svg"  => "svg",
            "tiff" => "tiff",
            "avif" => "avif",
            "heif" or "heic" => "heif",
            "bmp"  => "bmp",
            "raw"  => "raw",
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
