using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Image content plugin — files land in
/// <c>&lt;library&gt;\image\{creator}\{collection}\{title}.{ext}</c>.
/// Formats: JPEG, PNG, WebP, GIF, SVG, TIFF, AVIF, HEIF, BMP, RAW.
/// Mirrors <c>android-client/app/src/content/plugins/ImagePlugin.kt</c>.
/// </summary>
public sealed class ImagePlugin : ContentPluginBase
{
    public override string ContentType => "image";
    public override string DisplayName => "Image";
    public override string Glyph => "\uEB9F"; // Photo
    public override string Tagline => "Photos, art and scans";

    public override IReadOnlySet<string> SupportedMimeTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/svg+xml",
        "image/tiff", "image/avif", "image/heif", "image/bmp",
        "image/x-raw", "application/octet-stream",
    };

    private static readonly IReadOnlyList<FormOption> Formats =
    [
        new("jpeg", "JPEG"), new("png", "PNG"), new("webp", "WebP"), new("gif", "GIF"),
        new("svg", "SVG"), new("tiff", "TIFF"), new("avif", "AVIF"), new("heif", "HEIF"),
        new("bmp", "BMP"), new("raw", "RAW"),
    ];

    protected override string FormatMetadataKey => "imageFormat";
    protected override IReadOnlyList<FormOption> FormatOptions => Formats;
    protected override string DefaultFormat => "jpeg";

    protected override string MimeForFormat(string format) => format.ToLowerInvariant() switch
    {
        "jpeg" or "jpg" or "image/jpeg" => "image/jpeg",
        "png" or "image/png" => "image/png",
        "webp" or "image/webp" => "image/webp",
        "gif" or "image/gif" => "image/gif",
        "svg" or "image/svg+xml" => "image/svg+xml",
        "tiff" or "tif" or "image/tiff" => "image/tiff",
        "avif" or "image/avif" => "image/avif",
        "heif" or "heic" or "image/heif" => "image/heif",
        "bmp" or "image/bmp" => "image/bmp",
        "raw" or "image/x-raw" => "image/x-raw",
        _ => format.Contains('/') ? format : "application/octet-stream",
    };

    protected override string BuildRelativePath(ContentItem item)
    {
        var creator = Seg(item.Meta("creator"), "general");
        var collection = Seg(item.Meta("collection"), "general");
        var title = Seg(item.Title, "Unknown");
        return $"image/{creator}/{collection}/{title}.{ExtensionForItem(item)}";
    }

    // ── Browse / filter ──────────────────────────────────────

    public override IReadOnlyList<FormField> FilterFields { get; } =
    [
        FormField.Text("creator", "Creator / Photographer"),
        FormField.Text("collection", "Collection"),
        FormField.Text("keywords", "Keywords"),
        FormField.Text("cameraMake", "Camera make"),
    ];

    public override IReadOnlyDictionary<string, string> FilterableFields { get; } = new Dictionary<string, string>
    {
        ["creator"] = "Creator / Photographer",
        ["collection"] = "Collection",
        ["keywords"] = "Keywords",
        ["cameraMake"] = "Camera Make",
    };

    public override string SecondaryLine(ContentItem item) =>
        JoinNonBlank(item.Meta("creator"), item.Meta("collection"), item.Meta("keywords"));

    // ── Upload ───────────────────────────────────────────────

    public override IReadOnlyList<string> UploadFileExtensions { get; } =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".tif", ".tiff", ".avif", ".heic", ".heif", ".bmp"];

    public override IReadOnlyList<FormField> UploadFields { get; } =
    [
        FormField.Text("creator", "Creator / Photographer"),
        FormField.Text("collection", "Collection"),
        FormField.Text("copyright", "Copyright"),
        FormField.Text("keywords", "Keywords", placeholder: "comma-separated"),
        FormField.Text("cameraMake", "Camera make"),
        FormField.Text("cameraModel", "Camera model"),
        FormField.Date("dateTaken", "Date taken"),
    ];

    /// <summary>The format is detected from the picked file, so there is nothing to validate.</summary>
    public override string? ValidateUploadFields(IReadOnlyDictionary<string, string> fields) => null;

    public override Dictionary<string, string> BuildUploadMetadata(
        string title, IReadOnlyDictionary<string, string> fields) =>
        Metadata(title, fields,
            required: [],
            optional:
            [
                "creator", "collection", "copyright", "keywords", "cameraMake",
                "cameraModel", "dateTaken", "width", "height", "dpi", "colorSpace", "imageFormat",
            ]);

    /// <summary>The user never picks an image format; it is inferred from the file.</summary>
    public override string ResolveUploadMimeType(IReadOnlyDictionary<string, string> fields) =>
        MimeForFormat(Value(fields, "imageFormat", DefaultFormat));

    public override string ResolveUploadFormat(IReadOnlyDictionary<string, string> fields, string sourceFilePath)
    {
        var explicitFormat = Value(fields, "imageFormat");
        if (!string.IsNullOrWhiteSpace(explicitFormat)) return explicitFormat;

        var extension = MediaFileTools.ExtensionOf(sourceFilePath);
        return extension switch
        {
            "jpg" => "jpeg",
            "tif" => "tiff",
            "heic" => "heif",
            "" => DefaultFormat,
            _ => extension,
        };
    }

    public override Dictionary<string, string> ExtractFileMetadata(string filePath)
    {
        var metadata = TagTool.ReadImage(filePath);
        metadata["imageFormat"] = ResolveUploadFormat(new Dictionary<string, string>(), filePath);
        return metadata;
    }

    public override Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default) =>
        MediaFileTools.EditCopyAsync(filePath, temp => TagTool.WriteImage(temp, title, fields), ct);

    /// <summary>An image is its own thumbnail.</summary>
    public override byte[]? ExtractCoverArtFromFile(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length <= 24 * 1024 * 1024 ? File.ReadAllBytes(filePath) : null;
        }
        catch
        {
            return null;
        }
    }
}
