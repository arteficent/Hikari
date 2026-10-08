using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Core.Sync;

namespace Hikari.WindowsClient.Tests;

public class ContentSyncServiceTests
{
    private readonly string _library = TestEnvironment.NewLibraryRoot();
    private readonly SettingsRepository _settings = new();
    private readonly SyncPreferencesRepository _prefs = new();
    private readonly AudioPlugin _plugin = new();
    private readonly ContentSyncService _service;

    public ContentSyncServiceTests()
    {
        _settings.SaveLibraryRoot(_library);
        // Unroutable on purpose: none of these tests may reach a real server.
        _service = new ContentSyncService(new ApiClient(new AuthRepository()), _settings, "127.0.0.1:1", _prefs, _plugin);
    }

    [Fact]
    public async Task DownloadSkipsItemsAlreadyOnDiskWithoutReplacingThem()
    {
        var item = NewAudioItem();
        var path = WriteLocalFile(item, "original bytes");

        var result = await _service.DownloadItemsAsync([item]);

        Assert.Equal(new DownloadResult(Downloaded: 0, Skipped: 1, Failed: 0), result);
        Assert.Equal("original bytes", File.ReadAllText(path));
        Assert.Equal(_plugin.RelativePathFor(item), _prefs.LocalPathFor(item.Id));
    }

    [Fact]
    public async Task DownloadReportsProgressForEveryItem()
    {
        var items = new[] { NewAudioItem(), NewAudioItem() };
        foreach (var item in items) WriteLocalFile(item, "x");

        var reports = new List<SyncProgress>();
        await _service.DownloadItemsAsync(items, new SynchronousProgress<SyncProgress>(reports.Add));

        Assert.Equal(2, reports.Last().Completed);
        Assert.All(reports, r => Assert.Equal(2, r.Total));
    }

    [Fact]
    public async Task DownloadOfMissingItemSurfacesExpiredSession()
    {
        // No token is stored, so the client must route back to login rather than report a silent failure.
        await Assert.ThrowsAsync<AuthExpiredException>(() => _service.DownloadItemsAsync([NewAudioItem()]));
    }

    [Fact]
    public void RefreshDropsStaleEntriesOwnedByThisPluginOnly()
    {
        var staleAudio = Guid.NewGuid().ToString("N");
        var foreignBook = Guid.NewGuid().ToString("N");
        _prefs.SetSyncEntry(staleAudio, "audio/Nobody/Nothing/gone.mp3");
        _prefs.SetSyncEntry(foreignBook, "book/Someone/general/general/kept.epub");

        var result = _service.RefreshLocalState([]);

        Assert.True(result.Dropped >= 1);
        Assert.Null(_prefs.LocalPathFor(staleAudio));
        Assert.Equal("book/Someone/general/general/kept.epub", _prefs.LocalPathFor(foreignBook));
    }

    [Fact]
    public void RefreshRecordsKnownItemsFoundOnDisk()
    {
        var onDisk = NewAudioItem();
        var notOnDisk = NewAudioItem();
        WriteLocalFile(onDisk, "x");

        var result = _service.RefreshLocalState([onDisk, notOnDisk]);

        Assert.Equal(1, result.Found);
        Assert.Equal(_plugin.RelativePathFor(onDisk), _prefs.LocalPathFor(onDisk.Id));
        Assert.Null(_prefs.LocalPathFor(notOnDisk.Id));
    }

    [Fact]
    public void BulkSelectionPersistsInOneCall()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid().ToString("N")).ToList();

        _prefs.SetSyncEnabled(ids, true);
        Assert.All(ids, id => Assert.True(_prefs.IsMarked(id)));
        Assert.All(ids, id => Assert.True(new SyncPreferencesRepository().IsMarked(id)));

        _prefs.SetSyncEnabled(ids, false);
        Assert.All(ids, id => Assert.False(_prefs.IsMarked(id)));
    }

    [Fact]
    public async Task TruncatedDownloadIsNeverCommitted()
    {
        var item = NewAudioItem();
        item.SizeInBytes = 100;
        using var truncated = new MemoryStream(new byte[40]);

        await Assert.ThrowsAsync<IOException>(() => _plugin.SaveLocallyAsync(_library, item, truncated));

        var path = Path.Combine(_library, _plugin.RelativePathFor(item).Replace('/', Path.DirectorySeparatorChar));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task CompleteDownloadIsCommitted()
    {
        var item = NewAudioItem();
        item.SizeInBytes = 100;
        using var full = new MemoryStream(new byte[100]);

        var relative = await _plugin.SaveLocallyAsync(_library, item, full);

        Assert.Equal(100, new FileInfo(Path.Combine(_library, relative.Replace('/', Path.DirectorySeparatorChar))).Length);
    }

    [Theory]
    [InlineData(0, 0, 0, "Nothing to download.")]
    [InlineData(0, 3, 0, "Already downloaded.")]
    [InlineData(2, 1, 1, "2 downloaded, 1 already on this PC, 1 failed.")]
    public void DownloadResultDescribesOutcome(int downloaded, int skipped, int failed, string expected) =>
        Assert.Equal(expected, new DownloadResult(downloaded, skipped, failed).Describe());

    private static ContentItem NewAudioItem()
    {
        var id = Guid.NewGuid().ToString("N");
        return new ContentItem
        {
            Id = id,
            ContentType = "audio",
            Title = "Track " + id[..6],
            Format = "mp3",
            Metadata = new() { ["artist"] = "Artist", ["album"] = "Album", ["audioFormat"] = "mp3" },
        };
    }

    private string WriteLocalFile(ContentItem item, string content)
    {
        var path = Path.Combine(_library, _plugin.RelativePathFor(item).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
