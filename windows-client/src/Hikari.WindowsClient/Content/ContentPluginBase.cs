using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Content;

/// <summary>
/// Shared plumbing for every content plugin: library-relative path handling,
/// safe writes/deletes inside the library root, empty-folder pruning, format ⇄
/// extension ⇄ MIME mapping, and no-op defaults for the optional metadata hooks.
/// Concrete plugins supply only what actually differs between content types.
/// </summary>
public abstract class ContentPluginBase : IContentPlugin
{
    public abstract string ContentType { get; }
    public abstract string DisplayName { get; }
    public abstract string Glyph { get; }
    public abstract string Tagline { get; }

    public virtual string LocalDirectory => ContentType;

    public abstract IReadOnlySet<string> SupportedMimeTypes { get; }

    /// <summary>Metadata key holding this type's format, e.g. "audioFormat".</summary>
    protected abstract string FormatMetadataKey { get; }

    protected abstract IReadOnlyList<FormOption> FormatOptions { get; }

    protected abstract string DefaultFormat { get; }

    protected abstract string MimeForFormat(string format);

    /// <summary>Library-relative, forward-slashed path for an item.</summary>
    protected abstract string BuildRelativePath(ContentItem item);

    // ── Naming / MIME ────────────────────────────────────────

    public string RelativePathFor(ContentItem item) => BuildRelativePath(item);

    public virtual string MimeTypeFor(ContentItem item)
    {
        var format = FirstNonBlank(item.Meta(FormatMetadataKey), item.Format);
        return string.IsNullOrWhiteSpace(format) ? "application/octet-stream" : MimeForFormat(format);
    }

    protected string ExtensionForItem(ContentItem item)
    {
        var format = FirstNonBlank(item.Meta(FormatMetadataKey), item.Format);
        if (string.IsNullOrWhiteSpace(format)) return "bin";

        var match = FormatOptions.FirstOrDefault(o =>
            string.Equals(o.Value, format, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(MimeForFormat(o.Value), format, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match.Value;

        // Fall back to the sub-type of a MIME string we don't have an option for
        // ("image/x-canon-cr2" -> "x-canon-cr2" is useless, so only take it when
        // it looks like a plain extension).
        var candidate = format.Contains('/') ? format[(format.IndexOf('/') + 1)..] : format;
        candidate = new string(candidate.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrEmpty(candidate) ? "bin" : candidate;
    }

    protected static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    protected static string Seg(string? value, string fallback) =>
        PathSanitizer.Sanitize(string.IsNullOrWhiteSpace(value) ? fallback : value);

    // ── Local storage ────────────────────────────────────────

    /// <summary>Absolute path of this plugin's subtree inside the library.</summary>
    public string RootDirectory(string libraryRoot) => Path.Combine(libraryRoot, LocalDirectory);

    public async Task<string> SaveLocallyAsync(
        string libraryRoot, ContentItem item, Stream content, CancellationToken ct = default)
    {
        var relativePath = BuildRelativePath(item);
        var absolute = ResolveInsideLibrary(libraryRoot, relativePath);

        AppLog.Debug($"[{ContentType}] SaveLocally: {relativePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        // Write to a temp file first so an interrupted download can never leave a
        // half-written file that later looks like a complete, synced item.
        var temp = absolute + ".part";
        try
        {
            await using (var destination = new FileStream(
                temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(destination, 81920, ct).ConfigureAwait(false);
            }

            File.Move(temp, absolute, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return relativePath;
    }

    public bool DeleteLocally(string libraryRoot, string relativePath)
    {
        try
        {
            var absolute = ResolveInsideLibrary(libraryRoot, relativePath);
            var existed = File.Exists(absolute);

            AppLog.Debug($"[{ContentType}] DeleteLocally: {absolute}, exists={existed}");
            if (existed) File.Delete(absolute);

            PruneEmptyDirectories(libraryRoot, Path.GetDirectoryName(absolute));
            return existed;
        }
        catch (Exception ex)
        {
            AppLog.Error($"[{ContentType}] Error deleting {relativePath}", ex);
            return false;
        }
    }

    public IReadOnlyList<string> GetLocalItems(string libraryRoot)
    {
        try
        {
            var root = RootDirectory(libraryRoot);
            if (!Directory.Exists(root)) return Array.Empty<string>();

            return Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .Select(p => ToRelative(libraryRoot, p))
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error($"[{ContentType}] Error querying local {ContentType}", ex);
            return Array.Empty<string>();
        }
    }

    public string? GetLocalFile(string libraryRoot, ContentItem item)
    {
        try
        {
            var absolute = ResolveInsideLibrary(libraryRoot, BuildRelativePath(item));
            return File.Exists(absolute) ? absolute : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve a library-relative path to an absolute one, refusing anything that
    /// would escape the library root (defence against a hostile server crafting
    /// metadata like <c>../../Windows/System32</c>).
    /// </summary>
    protected static string ResolveInsideLibrary(string libraryRoot, string relativePath)
    {
        var rootFull = Path.GetFullPath(libraryRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Refusing to touch '{relativePath}' outside the library root.");
        }

        return combined;
    }

    private static string ToRelative(string libraryRoot, string absolutePath) =>
        Path.GetRelativePath(Path.GetFullPath(libraryRoot), absolutePath).Replace('\\', '/');

    /// <summary>
    /// Remove now-empty folders left behind by a delete, walking up but never past
    /// the library root. (The android client walks up until it hits the plugin's own
    /// folder, which lets a cross-type path escape the tree; anchoring on the root
    /// closes that hole.)
    /// </summary>
    private static void PruneEmptyDirectories(string libraryRoot, string? startDirectory)
    {
        var rootFull = Path.GetFullPath(libraryRoot);
        var current = startDirectory;

        while (!string.IsNullOrEmpty(current))
        {
            var currentFull = Path.GetFullPath(current);
            if (string.Equals(currentFull, rootFull, StringComparison.OrdinalIgnoreCase)) return;
            if (!currentFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(currentFull)) return;
            if (Directory.EnumerateFileSystemEntries(currentFull).Any()) return;

            try { Directory.Delete(currentFull); }
            catch { return; }

            current = Path.GetDirectoryName(currentFull);
        }
    }

    protected static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    // ── Browse / filter UI ───────────────────────────────────

    public abstract IReadOnlyList<FormField> FilterFields { get; }

    public abstract IReadOnlyDictionary<string, string> FilterableFields { get; }

    public abstract string SecondaryLine(ContentItem item);

    protected static string JoinNonBlank(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    // ── Upload ───────────────────────────────────────────────

    public abstract IReadOnlyList<string> UploadFileExtensions { get; }

    public virtual bool SupportsCoverImage => false;

    public virtual string CoverImageLabel => "Cover Image";

    public abstract IReadOnlyList<FormField> UploadFields { get; }

    public virtual string? ValidateUploadFields(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var field in UploadFields.Where(f => f.Required))
        {
            if (string.IsNullOrWhiteSpace(Value(fields, field.Key)))
            {
                return $"{field.Label.TrimEnd('*', ' ')} is required.";
            }
        }

        var format = Value(fields, FormatMetadataKey, DefaultFormat);
        if (FormatOptions.Count > 0 && FormatOptions.All(o => o.Value != format))
        {
            return $"Invalid {DisplayName.ToLowerInvariant()} format.";
        }

        return null;
    }

    public abstract Dictionary<string, string> BuildUploadMetadata(
        string title, IReadOnlyDictionary<string, string> fields);

    public virtual string ResolveUploadMimeType(IReadOnlyDictionary<string, string> fields) =>
        MimeForFormat(Value(fields, FormatMetadataKey, DefaultFormat));

    public virtual string ResolveUploadFormat(IReadOnlyDictionary<string, string> fields, string sourceFilePath) =>
        Value(fields, FormatMetadataKey, DefaultFormat);

    public virtual Dictionary<string, string> ExtractFileMetadata(string filePath) => new();

    public virtual Task<Stream> RewriteFileMetadataAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, string> fields,
        string? coverImagePath,
        CancellationToken ct = default) =>
        Task.FromResult<Stream>(File.OpenRead(filePath));

    public virtual byte[]? ExtractCoverArt(string libraryRoot, ContentItem item)
    {
        var file = GetLocalFile(libraryRoot, item);
        return file is null ? null : ExtractCoverArtFromFile(file);
    }

    public virtual byte[]? ExtractCoverArtFromFile(string filePath) => null;

    // ── Field helpers ────────────────────────────────────────

    protected static string Value(IReadOnlyDictionary<string, string> fields, string key, string fallback = "") =>
        fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    /// <summary>
    /// Seed a metadata dictionary with the title plus any required fields, then copy
    /// across every optional key that the user actually filled in — matching how the
    /// android plugins build their upload metadata.
    /// </summary>
    protected static Dictionary<string, string> Metadata(
        string title,
        IReadOnlyDictionary<string, string> fields,
        IEnumerable<KeyValuePair<string, string>> required,
        IEnumerable<string> optional)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["title"] = title };

        foreach (var (key, value) in required) meta[key] = value;

        foreach (var key in optional)
        {
            var value = Value(fields, key);
            if (!string.IsNullOrWhiteSpace(value)) meta[key] = value;
        }

        return meta;
    }
}
