using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Audio content plugin — files land in <c>&lt;library&gt;\audio\{artist}\{album}\{title}.{ext}</c>.
/// Formats: MP3, WAV, FLAC, AIFF, AAC, OGG, M4A.
/// Mirrors <c>android-client/app/src/content/plugins/AudioPlugin.kt</c>.
/// </summary>
public sealed class AudioPlugin : ContentPluginBase
{
    public override string ContentType => "audio";
    public override string DisplayName => "Audio";
    public override string Glyph => "\uE8D6"; // MusicInfo
    public override string Tagline => "Albums, singles and scores";

    public override IReadOnlySet<string> SupportedMimeTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/wav", "audio/flac", "audio/aiff",
        "audio/aac", "audio/mp4", "audio/ogg", "application/octet-stream",
    };

    private static readonly IReadOnlyList<FormOption> Formats =
    [
        new("mp3", "MP3"), new("wav", "WAV"), new("flac", "FLAC"), new("aiff", "AIFF"),
        new("aac", "AAC"), new("ogg", "OGG"), new("m4a", "M4A"),
    ];

    protected override string FormatMetadataKey => "audioFormat";
    protected override IReadOnlyList<FormOption> FormatOptions => Formats;
    protected override string DefaultFormat => "mp3";

    protected override string MimeForFormat(string format) => format.ToLowerInvariant() switch
    {
        "mp3" or "audio/mpeg" => "audio/mpeg",
        "wav" or "audio/wav" => "audio/wav",
        "flac" or "audio/flac" => "audio/flac",
        "aiff" or "audio/aiff" => "audio/aiff",
        "aac" or "audio/aac" => "audio/aac",
        "ogg" or "audio/ogg" => "audio/ogg",
        "m4a" or "audio/mp4" => "audio/mp4",
        _ => format.Contains('/') ? format : "application/octet-stream",
    };

    protected override string BuildRelativePath(ContentItem item)
    {
        var artist = Seg(item.Meta("artist"), "Unknown");
        var album = Seg(item.Meta("album"), "Unknown");
        var title = Seg(item.Title, "Unknown");
        return $"audio/{artist}/{album}/{title}.{ExtensionForItem(item)}";
    }

    // ── Browse / filter ──────────────────────────────────────

    public override IReadOnlyList<FormField> FilterFields { get; } =
    [
        FormField.Text("artist", "Artist"),
        FormField.Text("album", "Album"),
        FormField.Text("genre", "Genre"),
        FormField.Text("composer", "Composer"),
        FormField.Date("releaseFrom", "Released from"),
        FormField.Date("releaseTo", "Released to"),
    ];

    public override IReadOnlyDictionary<string, string> FilterableFields { get; } = new Dictionary<string, string>
    {
        ["artist"] = "Artist",
        ["album"] = "Album",
        ["genre"] = "Genre",
        ["composer"] = "Composer",
    };

    public override string SecondaryLine(ContentItem item) =>
        JoinNonBlank(item.Meta("artist"), item.Meta("album"), item.Meta("genre"));

    // ── Upload ───────────────────────────────────────────────

    public override IReadOnlyList<string> UploadFileExtensions { get; } =
        [".mp3", ".wav", ".flac", ".aiff", ".aif", ".aac", ".ogg", ".m4a"];

    public override bool SupportsCoverImage => true;
    public override string CoverImageLabel => "Album Art";

    public override IReadOnlyList<FormField> UploadFields { get; } =
    [
        FormField.Text("artist", "Artist", required: true),
        FormField.Text("album", "Album", required: true),
        FormField.Text("genre", "Genre", required: true),
        FormField.Dropdown("audioFormat", "Format", Formats, "mp3"),
        FormField.Text("composer", "Composer"),
        FormField.Text("trackNumber", "Track number"),
        FormField.Date("releaseDate", "Release date"),
        FormField.Text("language", "Language"),
    ];

    public override Dictionary<string, string> BuildUploadMetadata(
        string title, IReadOnlyDictionary<string, string> fields) =>
        Metadata(title, fields,
            required:
            [
                new("artist", Value(fields, "artist")),
                new("album", Value(fields, "album")),
                new("genre", Value(fields, "genre")),
                new("audioFormat", Value(fields, "audioFormat", "mp3")),
            ],
            optional:
            [
                "composer", "lyricist", "trackNumber", "albumArtist", "releaseDate",
                "language", "isrc", "publisher", "copyright", "producer", "label",
            ]);

    public override Dictionary<string, string> ExtractFileMetadata(string filePath) =>
        TagTool.ReadAudio(filePath);

    public override Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default) =>
        MediaFileTools.EditCopyAsync(
            filePath, temp => TagTool.WriteAudio(temp, title, fields, coverImagePath), ct);

    public override byte[]? ExtractCoverArtFromFile(string filePath) => TagTool.ReadCoverArt(filePath);
}
