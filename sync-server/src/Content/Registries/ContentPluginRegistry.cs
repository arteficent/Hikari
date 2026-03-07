using SyncServer.Content.Contracts;

namespace SyncServer.Content.Registries;

/// <summary>
/// Registry that holds all registered content plugins, keyed by ContentType.
/// Injected as a singleton.
/// </summary>
public interface IContentPluginRegistry
{
    IContentPlugin? Get(string contentType);
    IReadOnlyCollection<IContentPlugin> GetAll();
}

public class ContentPluginRegistry : IContentPluginRegistry
{
    private readonly Dictionary<string, IContentPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IContentPlugin plugin)
    {
        _plugins[plugin.ContentType] = plugin;
    }

    public IContentPlugin? Get(string contentType)
    {
        _plugins.TryGetValue(contentType, out var plugin);
        return plugin;
    }

    public IReadOnlyCollection<IContentPlugin> GetAll() => _plugins.Values;
}

/// <summary>
/// Extension methods for registering content plugins in DI.
/// </summary>
public static class ContentPluginServiceExtensions
{
    /// <summary>
    /// Call after all plugin registrations to wire up the registry.
    /// Usage: builder.Services.BuildContentPluginRegistry();
    /// This must be called once in Program.cs after all plugins are registered.
    /// </summary>
    public static IServiceCollection BuildContentPluginRegistry(this IServiceCollection services)
    {
        services.AddSingleton<ContentPluginRegistry>(sp =>
        {
            var registry = new ContentPluginRegistry();
            var plugins = sp.GetServices<IContentPlugin>();
            foreach (var plugin in plugins)
            {
                registry.Register(plugin);
            }
            return registry;
        });

        services.AddSingleton<IContentPluginRegistry>(sp => sp.GetRequiredService<ContentPluginRegistry>());

        return services;
    }
}
