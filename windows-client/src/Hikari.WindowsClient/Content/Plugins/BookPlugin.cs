using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Book content plugin — files land in
/// <c>&lt;library&gt;\book\{author}\{series}\{volume}\{title}.{ext}</c>.
/// Formats: EPUB, PDF, MOBI, AZW3, TXT, RTF, DOCX, HTML.
/// Mirrors <c>android-client/app/src/content/plugins/BookPlugin.kt</c>.
/// </summary>
public sealed class BookPlugin : ContentPluginBase
{
    public override string ContentType => "book";
    public override string DisplayName => "Book";
    public override string Glyph => "\uE8F1"; // Library
    public override string Tagline => "Novels, references and essays";

    public override IReadOnlySet<string> SupportedMimeTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/epub+zip", "application/pdf", "application/x-mobipocket-ebook",
        "application/vnd.amazon.ebook", "text/plain", "application/rtf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/html", "application/octet-stream",
    };

    private static readonly IReadOnlyList<FormOption> Formats =
    [
        new("epub", "EPUB"), new("pdf", "PDF"), new("mobi", "MOBI"), new("azw3", "AZW3"),
        new("txt", "TXT"), new("rtf", "RTF"), new("docx", "DOCX"), new("html", "HTML"),
    ];

    protected override string FormatMetadataKey => "bookFormat";
    protected override IReadOnlyList<FormOption> FormatOptions => Formats;
    protected override string DefaultFormat => "epub";

    protected override string MimeForFormat(string format) => format.ToLowerInvariant() switch
    {
        "epub" => "application/epub+zip",
        "pdf" => "application/pdf",
        "mobi" => "application/x-mobipocket-ebook",
        "azw3" => "application/vnd.amazon.ebook",
        "txt" => "text/plain",
        "rtf" => "application/rtf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "html" => "text/html",
        _ => format.Contains('/') ? format : "application/octet-stream",
    };

    protected override string BuildRelativePath(ContentItem item)
    {
        var author = Seg(item.Meta("author"), "Unknown");
        var series = Seg(item.Meta("series"), "general");
        var volume = Seg(item.Meta("volume"), "general");
        var title = Seg(item.Title, "Unknown");
        return $"book/{author}/{series}/{volume}/{title}.{ExtensionForItem(item)}";
    }

    // ── Browse / filter ──────────────────────────────────────

    public override IReadOnlyList<FormField> FilterFields { get; } =
    [
        FormField.Text("author", "Author"),
        FormField.Text("genre", "Genre"),
        FormField.Text("publisher", "Publisher"),
        FormField.Text("series", "Series"),
        FormField.Text("language", "Language"),
        FormField.Text("isbn", "ISBN"),
    ];

    public override IReadOnlyDictionary<string, string> FilterableFields { get; } = new Dictionary<string, string>
    {
        ["author"] = "Author",
        ["genre"] = "Genre",
        ["publisher"] = "Publisher",
        ["language"] = "Language",
        ["series"] = "Series",
        ["isbn"] = "ISBN",
    };

    public override string SecondaryLine(ContentItem item) =>
        JoinNonBlank(item.Meta("author"), item.Meta("series"), item.Meta("genre"));

    // ── Upload ───────────────────────────────────────────────

    public override IReadOnlyList<string> UploadFileExtensions { get; } =
        [".epub", ".pdf", ".mobi", ".azw3", ".txt", ".rtf", ".docx", ".html", ".htm"];

    public override IReadOnlyList<FormField> UploadFields { get; } =
    [
        FormField.Text("author", "Author", required: true),
        FormField.Dropdown("bookFormat", "Format", Formats, "epub", required: true),
        FormField.Text("isbn", "ISBN"),
        FormField.Text("genre", "Genre"),
        FormField.Text("publisher", "Publisher"),
        FormField.Text("pages", "Pages"),
        FormField.Text("language", "Language"),
        FormField.Text("series", "Series"),
        FormField.Text("volume", "Volume"),
        FormField.Date("publicationDate", "Publication date"),
    ];

    public override Dictionary<string, string> BuildUploadMetadata(
        string title, IReadOnlyDictionary<string, string> fields) =>
        Metadata(title, fields,
            required:
            [
                new("author", Value(fields, "author")),
                new("bookFormat", Value(fields, "bookFormat", "epub")),
            ],
            optional: ["isbn", "genre", "publisher", "pages", "language", "series", "volume", "publicationDate"]);

    public override Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default)
    {
        if (MediaFileTools.ExtensionOf(filePath) == "epub")
        {
            return MediaFileTools.EditCopyAsync(filePath, temp => ArchiveTool.RewriteEpub(temp, title, fields), ct);
        }

        // Other book formats have no portable metadata container we can safely
        // rewrite, so they upload byte-for-byte (matching stripGeneric on android).
        return Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public override byte[]? ExtractCoverArtFromFile(string filePath) =>
        MediaFileTools.ExtensionOf(filePath) == "epub" ? ArchiveTool.ExtractEpubCover(filePath) : null;
}
