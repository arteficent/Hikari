namespace Hikari.WindowsClient.Content;

/// <summary>
/// Registry holding all registered content plugins, keyed by content type.
/// Mirrors <c>android-client/app/src/content/ContentPluginRegistry.kt</c>.
/// </summary>
public sealed class ContentPluginRegistry
{
    private readonly Dictionary<string, IContentPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IContentPlugin> _ordered = new();

    public void Register(IContentPlugin plugin)
    {
        if (_plugins.TryAdd(plugin.ContentType, plugin))
        {
            _ordered.Add(plugin);
        }
        else
        {
            _plugins[plugin.ContentType] = plugin;
            var index = _ordered.FindIndex(p =>
                string.Equals(p.ContentType, plugin.ContentType, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _ordered[index] = plugin;
        }
    }

    public IContentPlugin? Get(string contentType) =>
        _plugins.TryGetValue(contentType, out var plugin) ? plugin : null;

    /// <summary>All plugins, in registration order.</summary>
    public IReadOnlyList<IContentPlugin> GetAll() => _ordered;

    public IReadOnlyCollection<string> ContentTypes() => _plugins.Keys;
}
