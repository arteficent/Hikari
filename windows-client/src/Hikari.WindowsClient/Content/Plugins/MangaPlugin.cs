using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Manga content plugin — files land in
/// <c>&lt;library&gt;\manga\{author}\{series}\{volume}\{title}.{ext}</c>.
/// Formats: CBZ, CBR, PDF, EPUB, ZIP.
/// Mirrors <c>android-client/app/src/content/plugins/MangaPlugin.kt</c>.
/// </summary>
public sealed class MangaPlugin : ContentPluginBase
{
    public override string ContentType => "manga";
    public override string DisplayName => "Manga";
    public override string Glyph => "\uE736"; // Comic-style page
    public override string Tagline => "Series, volumes and one-shots";

    public override IReadOnlySet<string> SupportedMimeTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-cbz", "application/x-cbr", "application/pdf",
        "application/epub+zip", "application/zip", "application/octet-stream",
    };

    private static readonly IReadOnlyList<FormOption> Formats =
    [
        new("cbz", "CBZ"), new("cbr", "CBR"), new("pdf", "PDF"),
        new("epub", "EPUB"), new("zip", "ZIP"),
    ];

    protected override string FormatMetadataKey => "mangaFormat";
    protected override IReadOnlyList<FormOption> FormatOptions => Formats;
    protected override string DefaultFormat => "cbz";

    protected override string MimeForFormat(string format) => format.ToLowerInvariant() switch
    {
        "cbz" => "application/x-cbz",
        "cbr" => "application/x-cbr",
        "pdf" => "application/pdf",
        "epub" => "application/epub+zip",
        "zip" => "application/zip",
        _ => format.Contains('/') ? format : "application/octet-stream",
    };

    protected override string BuildRelativePath(ContentItem item)
    {
        var author = Seg(item.Meta("author"), "Unknown");
        var series = Seg(item.Meta("series"), "general");
        var volume = Seg(item.Meta("volume"), "general");
        var title = Seg(item.Title, "Unknown");
        return $"manga/{author}/{series}/{volume}/{title}.{ExtensionForItem(item)}";
    }

    // ── Browse / filter ──────────────────────────────────────

    public override IReadOnlyList<FormField> FilterFields { get; } =
    [
        FormField.Text("author", "Author"),
        FormField.Text("artist", "Artist"),
        FormField.Text("genre", "Genre"),
        FormField.Text("series", "Series"),
        FormField.Text("status", "Status", placeholder: "ongoing / completed"),
        FormField.Text("demographic", "Demographic"),
    ];

    public override IReadOnlyDictionary<string, string> FilterableFields { get; } = new Dictionary<string, string>
    {
        ["author"] = "Author",
        ["artist"] = "Artist",
        ["genre"] = "Genre",
        ["status"] = "Status (ongoing/completed)",
        ["demographic"] = "Demographic",
        ["language"] = "Language",
    };

    public override string SecondaryLine(ContentItem item) =>
        JoinNonBlank(item.Meta("author"), item.Meta("series"), item.Meta("volume"), item.Meta("status"));

    // ── Upload ───────────────────────────────────────────────

    public override IReadOnlyList<string> UploadFileExtensions { get; } =
        [".cbz", ".cbr", ".pdf", ".epub", ".zip"];

    public override IReadOnlyList<FormField> UploadFields { get; } =
    [
        FormField.Text("author", "Author", required: true),
        FormField.Dropdown("mangaFormat", "Format", Formats, "cbz", required: true),
        FormField.Text("artist", "Artist / Illustrator"),
        FormField.Text("genre", "Genre"),
        FormField.Text("series", "Series"),
        FormField.Text("volume", "Volume"),
        FormField.Text("chapters", "Chapters"),
        FormField.Text("volumes", "Volumes"),
        FormField.Text("status", "Status", placeholder: "ongoing / completed / hiatus"),
        FormField.Text("demographic", "Demographic", placeholder: "shounen / seinen / shoujo / josei"),
        FormField.Text("language", "Language"),
        FormField.Date("releaseDate", "Release date"),
    ];

    public override Dictionary<string, string> BuildUploadMetadata(
        string title, IReadOnlyDictionary<string, string> fields) =>
        Metadata(title, fields,
            required:
            [
                new("author", Value(fields, "author")),
                new("mangaFormat", Value(fields, "mangaFormat", "cbz")),
            ],
            optional:
            [
                "artist", "genre", "series", "volume", "chapters", "volumes",
                "status", "demographic", "language", "releaseDate",
            ]);

    public override Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default) =>
        MediaFileTools.ExtensionOf(filePath) switch
        {
            "epub" => MediaFileTools.EditCopyAsync(filePath, temp => ArchiveTool.RewriteEpub(temp, title, fields), ct),
            "cbz" or "zip" => MediaFileTools.EditCopyAsync(filePath, temp => ArchiveTool.RewriteCbz(temp, title, fields), ct),
            _ => Task.FromResult<Stream>(File.OpenRead(filePath)),
        };

    public override byte[]? ExtractCoverArtFromFile(string filePath) =>
        MediaFileTools.ExtensionOf(filePath) switch
        {
            "cbz" or "zip" => ArchiveTool.ExtractCbzCover(filePath),
            "epub" => ArchiveTool.ExtractEpubCover(filePath),
            _ => null,
        };
}
