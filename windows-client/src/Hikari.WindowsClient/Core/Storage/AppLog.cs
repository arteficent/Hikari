using System.Diagnostics;
using System.Text;

namespace Hikari.WindowsClient.Core.Storage;

/// <summary>
/// Minimal file + debug logger, the desktop stand-in for android's <c>Log.d</c>.
/// Writes to <c>%LOCALAPPDATA%\Hikari\hikari-client.log</c>.
/// </summary>
public static class AppLog
{
    private static readonly Lock Gate = new();
    private static readonly string LogFile = Path.Combine(AppPaths.PreferencesDirectory, "hikari-client.log");
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Debug(string message) => Write("DEBUG", message);
    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Trace.WriteLine(line);

        try
        {
            lock (Gate)
            {
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxBytes)
                {
                    File.Move(LogFile, LogFile + ".1", overwrite: true);
                }

                File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
