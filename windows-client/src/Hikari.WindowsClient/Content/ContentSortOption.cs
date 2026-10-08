using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Content;

/// <summary>
/// A field the content list can be sorted by, offered in the sort selector.
/// Mirrors <c>ContentSortOption</c> in <c>android-client/app/src/content/ContentPlugin.kt</c>.
/// </summary>
public sealed record ContentSortOption(string Key, string Label, IComparer<ContentItem> Comparer)
{
    /// <summary>Sort options every content type offers regardless of its metadata schema.</summary>
    public static IReadOnlyList<ContentSortOption> Base { get; } =
    [
        new("dateAdded", "Date Added", By(i => i.CreatedAt)),
        new("modified", "Date Modified", By(i => i.LastModified)),
        new("name", "Name", By(i => i.Title)),
    ];

    /// <summary>Sort option for a plugin-specific metadata field, e.g. album or author.</summary>
    public static ContentSortOption Metadata(string key, string label) => new(key, label, By(i => i.Meta(key)));

    public static IReadOnlyList<ContentSortOption> BaseWith(params ContentSortOption[] extra) => [.. Base, .. extra];

    // Missing values sort first, matching kotlin's compareValues on nulls.
    private static IComparer<ContentItem> By(Func<ContentItem, string?> selector) =>
        Comparer<ContentItem>.Create((a, b) =>
            string.Compare(selector(a) ?? string.Empty, selector(b) ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Label;
}
