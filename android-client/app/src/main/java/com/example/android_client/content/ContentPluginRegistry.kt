package com.example.android_client.content

/**
 * Registry holding all registered content plugins, keyed by contentType.
 */
class ContentPluginRegistry {
    private val plugins = mutableMapOf<String, ContentPlugin>()

    fun register(plugin: ContentPlugin) {
        plugins[plugin.contentType] = plugin
    }

    fun get(contentType: String): ContentPlugin? = plugins[contentType]

    fun getAll(): Collection<ContentPlugin> = plugins.values

    fun contentTypes(): Set<String> = plugins.keys
}
