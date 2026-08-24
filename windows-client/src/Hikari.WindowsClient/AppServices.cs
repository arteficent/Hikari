using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Core.Sync;

namespace Hikari.WindowsClient;

/// <summary>
/// Composition root. The android client news these up in <c>MainActivity.onCreate</c>;
/// a WinUI app has no equivalent single activity, so they live here and are created
/// once at startup.
/// </summary>
public static class AppServices
{
    public static SettingsRepository Settings { get; } = new();
    public static AuthRepository Auth { get; } = new();
    public static SyncPreferencesRepository SyncPreferences { get; } = new();
    public static ApiClient Api { get; } = new(Auth);
    public static ContentPluginRegistry Plugins { get; } = BuildRegistry();

    /// <summary>The server the app is currently pointed at. Never null past the server screen.</summary>
    public static string ServerDomain => Settings.ServerDomain ?? string.Empty;

    public static JwtDecoder.Claims? CurrentClaims => JwtDecoder.Decode(Auth.Token);

    public static bool IsAdmin => CurrentClaims?.IsAdmin == true;

    public static bool IsRoot => CurrentClaims?.IsRoot == true;

    public static ContentSyncService SyncServiceFor(IContentPlugin plugin) =>
        new(Api, Settings, ServerDomain, SyncPreferences, plugin);

    private static ContentPluginRegistry BuildRegistry()
    {
        var registry = new ContentPluginRegistry();

        // Register content plugins (add new plugins here).
        registry.Register(new AudioPlugin());
        registry.Register(new VideoPlugin());
        registry.Register(new BookPlugin());
        registry.Register(new MangaPlugin());
        registry.Register(new ImagePlugin());

        return registry;
    }
}
