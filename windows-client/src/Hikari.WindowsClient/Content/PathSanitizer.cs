using System.Text;

namespace Hikari.WindowsClient.Content;

/// <summary>
/// Sanitize a single path segment so it is safe on every filesystem the library
/// might live on (NTFS, exFAT/FAT on removable drives, SMB shares). Strips the
/// characters reserved by Windows and FAT, drops control characters, trims
/// trailing dots/spaces (also illegal on Windows), and converts spaces to '-' so
/// paths stay shell-friendly.
///
/// Mirrors <c>android-client/app/src/content/plugins/PathSanitizer.kt</c>, plus
/// the Windows-only reserved device names (CON, PRN, NUL, COM1…), which would
/// otherwise make a file impossible to create.
/// </summary>
public static class PathSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private const int MaxSegmentLength = 120;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "Unknown";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch < 0x20 || ch == 0x7F) continue;

            builder.Append(ch switch
            {
                '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' => '-',
                ' ' => '-',
                _ => ch,
            });
        }

        var cleaned = CollapseDashes(builder.ToString()).Trim('-', '.', ' ');

        if (cleaned.Length > MaxSegmentLength)
        {
            cleaned = cleaned[..MaxSegmentLength].TrimEnd('-', '.', ' ');
        }

        if (cleaned.Length == 0) return "Unknown";

        // "CON.mp3" is just as unusable as "CON" on Windows, so compare the stem.
        var stem = Path.GetFileNameWithoutExtension(cleaned);
        if (ReservedNames.Contains(stem) || ReservedNames.Contains(cleaned))
        {
            cleaned = "_" + cleaned;
        }

        return cleaned;
    }

    private static string CollapseDashes(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var ch in value)
        {
            if (ch == '-')
            {
                if (previousWasDash) continue;
                previousWasDash = true;
            }
            else
            {
                previousWasDash = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
