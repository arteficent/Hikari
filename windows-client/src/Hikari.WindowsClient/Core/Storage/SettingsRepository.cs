namespace Hikari.WindowsClient.Core.Storage;

/// <summary>
/// App settings. Mirrors <c>android-client/app/src/core/storage/SettingsRepository.kt</c>
/// and adds <see cref="LibraryRoot"/>, which is fixed to <c>/sdcard/Hikari</c> on
/// android but configurable on the desktop (external drive, NAS share, …).
/// </summary>
public sealed class SettingsRepository : JsonPreferenceStore<SettingsRepository.SettingsState>
{
    public sealed class SettingsState
    {
        public string? ServerDomain { get; set; }
        public string? ThemeName { get; set; }
        public string? LibraryRoot { get; set; }
    }

    public SettingsRepository() : base("settings.json") { }

    public string? ServerDomain => Read(s => s.ServerDomain);

    public string ThemeName => Read(s => string.IsNullOrWhiteSpace(s.ThemeName) ? "Wisteria" : s.ThemeName!);

    /// <summary>Root folder that downloaded content is mirrored into.</summary>
    public string LibraryRoot =>
        Read(s => string.IsNullOrWhiteSpace(s.LibraryRoot) ? AppPaths.DefaultLibraryRoot : s.LibraryRoot!);

    public void SaveServerDomain(string domain)
    {
        AppLog.Debug($"SettingsRepository.SaveServerDomain({domain})");
        Mutate(s => s.ServerDomain = domain);
    }

    public void ClearServerDomain()
    {
        AppLog.Debug("SettingsRepository.ClearServerDomain()");
        Mutate(s => s.ServerDomain = null);
    }

    public void SaveTheme(string name) => Mutate(s => s.ThemeName = name);

    public void SaveLibraryRoot(string path)
    {
        AppLog.Debug($"SettingsRepository.SaveLibraryRoot({path})");
        Mutate(s => s.LibraryRoot = path);
    }
}
