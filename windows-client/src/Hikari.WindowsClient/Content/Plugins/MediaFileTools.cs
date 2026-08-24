using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Content.Plugins;

/// <summary>
/// Scratch-file helpers shared by the metadata rewriters. Tag libraries want a
/// real file to work on, so uploads are staged into <c>%TEMP%\Hikari</c>, edited
/// in place, and streamed from there. The returned stream owns the scratch file
/// and deletes it on dispose.
/// </summary>
public static class MediaFileTools
{
    private static string TempRoot => AppPaths.EnsureDirectory(Path.Combine(Path.GetTempPath(), "Hikari"));

    public static string NewTempPath(string extension)
    {
        var suffix = string.IsNullOrWhiteSpace(extension) ? ".bin"
            : extension.StartsWith('.') ? extension : "." + extension;
        return Path.Combine(TempRoot, $"upload-{Guid.NewGuid():N}{suffix}");
    }

    public static async Task<string> CopyToTempAsync(string sourcePath, CancellationToken ct = default)
    {
        var temp = NewTempPath(Path.GetExtension(sourcePath));

        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var destination = new FileStream(
            temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        await source.CopyToAsync(destination, 81920, ct).ConfigureAwait(false);
        return temp;
    }

    /// <summary>Open a scratch file for reading; the file is removed when the stream closes.</summary>
    public static Stream OpenTempForRead(string tempPath) =>
        new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);

    /// <summary>
    /// Copy the source into a scratch file, run <paramref name="edit"/> over it, and
    /// hand back a read stream. If the edit throws, the original bytes are uploaded
    /// unchanged rather than failing the whole upload.
    /// </summary>
    public static async Task<Stream> EditCopyAsync(
        string sourcePath, Action<string> edit, CancellationToken ct = default)
    {
        var temp = await CopyToTempAsync(sourcePath, ct).ConfigureAwait(false);

        try
        {
            edit(temp);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Metadata rewrite failed for {Path.GetFileName(sourcePath)}: {ex.Message}. Uploading as-is.");
        }

        return OpenTempForRead(temp);
    }

    public static string ExtensionOf(string filePath) =>
        Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

    public static void CleanTempDirectory()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;

            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (var file in Directory.EnumerateFiles(TempRoot))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                }
                catch { /* still in use */ }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Temp cleanup failed: {ex.Message}");
        }
    }
}
