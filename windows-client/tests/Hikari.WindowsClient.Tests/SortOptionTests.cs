using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Network;

namespace Hikari.WindowsClient.Tests;

public class SortOptionTests
{
    public static TheoryData<IContentPlugin> Plugins =>
        [new AudioPlugin(), new VideoPlugin(), new BookPlugin(), new MangaPlugin(), new ImagePlugin()];

    [Theory]
    [MemberData(nameof(Plugins))]
    public void EveryPluginOffersBaseSortsFirstPlusItsOwnFields(IContentPlugin plugin)
    {
        var keys = plugin.SortOptions.Select(o => o.Key).ToList();

        Assert.Equal(["dateAdded", "modified", "name"], keys.Take(3));
        Assert.True(keys.Count > 3, $"{plugin.ContentType} should add metadata sort options");
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void AudioOffersAlbumAndAuthorSorts()
    {
        var labels = new AudioPlugin().SortOptions.Select(o => o.Label).ToList();

        Assert.Contains("Date Added", labels);
        Assert.Contains("Date Modified", labels);
        Assert.Contains("Name", labels);
        Assert.Contains("Album Name", labels);
        Assert.Contains("Author Name", labels);
    }

    [Fact]
    public void MetadataSortIsCaseInsensitiveWithMissingValuesFirst()
    {
        var byAlbum = new AudioPlugin().SortOptions.Single(o => o.Key == "album");
        var items = new List<ContentItem>
        {
            Item("1", album: "beta"),
            Item("2", album: "Alpha"),
            Item("3", album: null),
        };

        items.Sort(byAlbum.Comparer);

        Assert.Equal(["3", "2", "1"], items.Select(i => i.Id));
    }

    [Fact]
    public void DateSortsOrderIsoTimestampsChronologically()
    {
        var byAdded = ContentSortOption.Base.Single(o => o.Key == "dateAdded");
        var byModified = ContentSortOption.Base.Single(o => o.Key == "modified");
        var items = new List<ContentItem>
        {
            new() { Id = "late", CreatedAt = "2026-05-01T00:00:00Z", LastModified = "2026-01-01T00:00:00Z" },
            new() { Id = "early", CreatedAt = "2025-12-31T23:59:59Z", LastModified = "2026-06-01T00:00:00Z" },
        };

        Assert.Equal(["early", "late"], items.Order(byAdded.Comparer).Select(i => i.Id));
        Assert.Equal(["late", "early"], items.Order(byModified.Comparer).Select(i => i.Id));
    }

    [Fact]
    public void NameSortIgnoresCase()
    {
        var byName = ContentSortOption.Base.Single(o => o.Key == "name");
        var items = new[] { new ContentItem { Id = "z", Title = "zebra" }, new ContentItem { Id = "a", Title = "Apple" } };

        Assert.Equal(["a", "z"], items.Order(byName.Comparer).Select(i => i.Id));
    }

    private static ContentItem Item(string id, string? album) => new()
    {
        Id = id,
        Title = id,
        Metadata = album is null ? new() : new() { ["album"] = album },
    };
}
