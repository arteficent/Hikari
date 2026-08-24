using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// EPUB and CBZ handling — rewriting the embedded metadata document and pulling
/// out cover art. Mirrors the <c>stripEpub</c> / <c>stripCbz</c> paths of the
/// android client's <c>FileMetadataStripper</c>.
/// </summary>
public static class ArchiveTool
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif"];

    // ── EPUB ─────────────────────────────────────────────────

    /// <summary>
    /// Replace the OPF <c>&lt;metadata&gt;</c> block with values from the upload form,
    /// dropping whatever the source file carried.
    /// </summary>
    public static void RewriteEpub(string epubPath, string title, IReadOnlyDictionary<string, string> fields)
    {
        using var archive = ZipFile.Open(epubPath, ZipArchiveMode.Update);

        var opfPath = FindOpfPath(archive);
        if (opfPath is null)
        {
            AppLog.Debug("RewriteEpub: no OPF found; leaving file untouched.");
            return;
        }

        var opfEntry = archive.GetEntry(opfPath);
        if (opfEntry is null) return;

        XDocument document;
        using (var reader = new StreamReader(opfEntry.Open(), Encoding.UTF8))
        {
            document = XDocument.Load(reader);
        }

        var packageNs = document.Root?.Name.Namespace ?? Opf;
        var metadata = document.Root?.Element(packageNs + "metadata");
        if (metadata is null) return;

        // Keep <meta> hints that point at the cover image, otherwise readers lose
        // the artwork we are deliberately preserving.
        var preserved = metadata.Elements()
            .Where(e => e.Name.LocalName == "meta" &&
                        string.Equals((string?)e.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(e => new XElement(e))
            .ToList();

        metadata.RemoveNodes();

        metadata.Add(new XElement(Dc + "title", title));
        AddIfPresent(metadata, Dc + "creator", Get(fields, "author"));
        AddIfPresent(metadata, Dc + "contributor", Get(fields, "artist"));
        AddIfPresent(metadata, Dc + "publisher", Get(fields, "publisher"));
        AddIfPresent(metadata, Dc + "language", Get(fields, "language"));
        AddIfPresent(metadata, Dc + "subject", Get(fields, "genre"));
        AddIfPresent(metadata, Dc + "date", Get(fields, "publicationDate", Get(fields, "releaseDate")));
        AddIfPresent(metadata, Dc + "description", Get(fields, "description"));

        var isbn = Get(fields, "isbn");
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            metadata.Add(new XElement(Dc + "identifier",
                new XAttribute(Opf + "scheme", "ISBN"), isbn));
        }

        foreach (var element in preserved) metadata.Add(element);

        // ZipArchiveMode.Update keeps the old bytes unless the entry is truncated first.
        using var stream = opfEntry.Open();
        stream.SetLength(0);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer);
    }

    public static byte[]? ExtractEpubCover(string epubPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(epubPath);

            var opfPath = FindOpfPath(archive);
            if (opfPath is not null)
            {
                var opfEntry = archive.GetEntry(opfPath);
                if (opfEntry is not null)
                {
                    XDocument document;
                    using (var reader = new StreamReader(opfEntry.Open(), Encoding.UTF8))
                    {
                        document = XDocument.Load(reader);
                    }

                    var coverHref = FindCoverHref(document);
                    if (coverHref is not null)
                    {
                        var basePath = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
                        var full = string.IsNullOrEmpty(basePath) ? coverHref : $"{basePath}/{coverHref}";
                        var entry = archive.GetEntry(Normalize(full)) ?? archive.GetEntry(coverHref);
                        if (entry is not null) return ReadEntry(entry);
                    }
                }
            }

            // No manifest hint — fall back to the first image in the archive.
            return ReadFirstImage(archive);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ExtractEpubCover failed: {ex.Message}");
            return null;
        }
    }

    private static string? FindOpfPath(ZipArchive archive)
    {
        var containerEntry = archive.GetEntry("META-INF/container.xml");
        if (containerEntry is not null)
        {
            try
            {
                using var reader = new StreamReader(containerEntry.Open(), Encoding.UTF8);
                var document = XDocument.Load(reader);
                var fullPath = document.Descendants(Container + "rootfile").FirstOrDefault()
                                   ?.Attribute("full-path")?.Value
                               ?? document.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")
                                   ?.Attribute("full-path")?.Value;

                if (!string.IsNullOrWhiteSpace(fullPath)) return Normalize(fullPath);
            }
            catch
            {
                // Malformed container.xml; fall through to the scan below.
            }
        }

        return archive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))
            ?.FullName;
    }

    private static string? FindCoverHref(XDocument opf)
    {
        var manifestItems = opf.Descendants().Where(e => e.Name.LocalName == "item").ToList();

        var coverId = opf.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "meta" &&
                                 string.Equals((string?)e.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("content")?.Value;

        if (!string.IsNullOrWhiteSpace(coverId))
        {
            var byId = manifestItems.FirstOrDefault(e =>
                string.Equals((string?)e.Attribute("id"), coverId, StringComparison.OrdinalIgnoreCase));
            if (byId?.Attribute("href")?.Value is { Length: > 0 } href) return href;
        }

        var byProperty = manifestItems.FirstOrDefault(e =>
            ((string?)e.Attribute("properties"))?.Contains("cover-image", StringComparison.OrdinalIgnoreCase) == true);
        if (byProperty?.Attribute("href")?.Value is { Length: > 0 } propHref) return propHref;

        return manifestItems
            .FirstOrDefault(e =>
                ((string?)e.Attribute("media-type"))?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true &&
                ((string?)e.Attribute("id"))?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true)
            ?.Attribute("href")?.Value;
    }

    // ── CBZ ──────────────────────────────────────────────────

    /// <summary>Replace the ComicInfo.xml sidecar with values from the upload form.</summary>
    public static void RewriteCbz(string cbzPath, string title, IReadOnlyDictionary<string, string> fields)
    {
        using var archive = ZipFile.Open(cbzPath, ZipArchiveMode.Update);

        foreach (var stale in archive.Entries
                     .Where(e => e.Name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            stale.Delete();
        }

        var comicInfo = new XElement("ComicInfo",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement("Title", title));

        AddIfPresent(comicInfo, "Series", Get(fields, "series"));
        AddIfPresent(comicInfo, "Number", Get(fields, "volume"));
        AddIfPresent(comicInfo, "Count", Get(fields, "volumes"));
        AddIfPresent(comicInfo, "Writer", Get(fields, "author"));
        AddIfPresent(comicInfo, "Penciller", Get(fields, "artist"));
        AddIfPresent(comicInfo, "Genre", Get(fields, "genre"));
        AddIfPresent(comicInfo, "LanguageISO", Get(fields, "language"));

        var status = Get(fields, "status");
        if (!string.IsNullOrWhiteSpace(status))
        {
            comicInfo.Add(new XElement("Notes", $"Status: {status}"));
        }

        var entry = archive.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        new XDocument(comicInfo).Save(writer);
    }

    public static byte[]? ExtractCbzCover(string cbzPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(cbzPath);
            return ReadFirstImage(archive);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ExtractCbzCover failed: {ex.Message}");
            return null;
        }
    }

    // ── Shared helpers ───────────────────────────────────────

    private static byte[]? ReadFirstImage(ZipArchive archive)
    {
        var entry = archive.Entries
            .Where(e => e.Length > 0 && ImageExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return entry is null ? null : ReadEntry(entry);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AddIfPresent(XElement parent, XName name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parent.Add(new XElement(name, value));
    }

    private static string Get(IReadOnlyDictionary<string, string> fields, string key, string fallback = "") =>
        fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
