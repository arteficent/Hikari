using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Video content plugin — files land in
/// <c>&lt;library&gt;\video\{type}\{series}\{season}\{episode}\{title}.{ext}</c>.
/// Formats: MP4, MOV, AVI, MKV, WMV, WebM, FLV.
/// Mirrors <c>android-client/app/src/content/plugins/VideoPlugin.kt</c>.
/// </summary>
public sealed class VideoPlugin : ContentPluginBase
{
    public override string ContentType => "video";
    public override string DisplayName => "Video";
    public override string Glyph => "\uE8B2"; // Video
    public override string Tagline => "Films, series and animation";

    public override IReadOnlySet<string> SupportedMimeTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "video/quicktime", "video/x-msvideo", "video/x-matroska",
        "video/x-ms-wmv", "video/webm", "video/x-flv", "application/octet-stream",
    };

    private static readonly IReadOnlyList<FormOption> Formats =
    [
        new("mp4", "MP4"), new("mov", "MOV"), new("avi", "AVI"), new("mkv", "MKV"),
        new("wmv", "WMV"), new("webm", "WebM"), new("flv", "FLV"),
    ];

    private static readonly IReadOnlyList<FormOption> Types =
    [
        new("animation", "Animation"), new("live", "Live"),
    ];

    protected override string FormatMetadataKey => "videoFormat";
    protected override IReadOnlyList<FormOption> FormatOptions => Formats;
    protected override string DefaultFormat => "mp4";

    protected override string MimeForFormat(string format) => format.ToLowerInvariant() switch
    {
        "mp4" or "video/mp4" => "video/mp4",
        "mov" or "video/quicktime" => "video/quicktime",
        "avi" or "video/x-msvideo" => "video/x-msvideo",
        "mkv" or "video/x-matroska" => "video/x-matroska",
        "wmv" or "video/x-ms-wmv" => "video/x-ms-wmv",
        "webm" or "video/webm" => "video/webm",
        "flv" or "video/x-flv" => "video/x-flv",
        _ => format.Contains('/') ? format : "application/octet-stream",
    };

    protected override string BuildRelativePath(ContentItem item)
    {
        var type = Seg(item.Meta("type"), "general");
        var series = Seg(item.Meta("series"), "general");
        var season = Seg(item.Meta("season"), "general");
        var episode = Seg(item.Meta("episode"), "general");
        var title = Seg(item.Title, "Unknown");
        return $"video/{type}/{series}/{season}/{episode}/{title}.{ExtensionForItem(item)}";
    }

    // ── Browse / filter ──────────────────────────────────────

    public override IReadOnlyList<FormField> FilterFields { get; } =
    [
        FormField.Text("genre", "Genre"),
        FormField.Text("director", "Director"),
        FormField.Text("series", "Series"),
        FormField.Text("season", "Season"),
        FormField.Text("episode", "Episode"),
        FormField.Dropdown("type", "Type", [new("", "Any"), .. Types]),
    ];

    public override IReadOnlyDictionary<string, string> FilterableFields { get; } = new Dictionary<string, string>
    {
        ["genre"] = "Genre",
        ["director"] = "Director",
        ["type"] = "Type (animation/live)",
        ["series"] = "Series",
        ["season"] = "Season",
        ["episode"] = "Episode",
    };

    public override string SecondaryLine(ContentItem item) =>
        JoinNonBlank(item.Meta("series"), item.Meta("season"), item.Meta("episode"), item.Meta("genre"));

    // ── Upload ───────────────────────────────────────────────

    public override IReadOnlyList<string> UploadFileExtensions { get; } =
        [".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".flv"];

    public override IReadOnlyList<FormField> UploadFields { get; } =
    [
        FormField.Dropdown("videoFormat", "Format", Formats, "mp4", required: true),
        FormField.Text("genre", "Genre"),
        FormField.Text("director", "Director"),
        FormField.Dropdown("type", "Type", Types, "animation"),
        FormField.Text("series", "Series"),
        FormField.Text("season", "Season"),
        FormField.Text("episode", "Episode"),
        FormField.Text("resolution", "Resolution", placeholder: "1920x1080"),
        FormField.Text("codec", "Codec", placeholder: "H.264"),
        FormField.Text("fps", "FPS"),
        FormField.Date("releaseDate", "Release date"),
        FormField.Text("language", "Language"),
    ];

    public override string? ValidateUploadFields(IReadOnlyDictionary<string, string> fields)
    {
        var baseError = base.ValidateUploadFields(fields);
        if (baseError is not null) return baseError;

        var type = Value(fields, "type");
        if (!string.IsNullOrWhiteSpace(type) && Types.All(t => t.Value != type))
        {
            return "Type must be 'animation' or 'live'.";
        }

        return null;
    }

    public override Dictionary<string, string> BuildUploadMetadata(
        string title, IReadOnlyDictionary<string, string> fields) =>
        Metadata(title, fields,
            required: [new("videoFormat", Value(fields, "videoFormat", "mp4"))],
            optional:
            [
                "genre", "director", "type", "series", "season", "episode", "resolution",
                "codec", "fps", "releaseDate", "language", "duration", "bitrate", "subtitleLanguages",
            ]);

    public override Dictionary<string, string> ExtractFileMetadata(string filePath) =>
        TagTool.ReadVideo(filePath);

    public override Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default) =>
        MediaFileTools.EditCopyAsync(filePath, temp => TagTool.WriteVideo(temp, title, fields), ct);

    public override byte[]? ExtractCoverArtFromFile(string filePath) => TagTool.ReadCoverArt(filePath);
}
