using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Views;

namespace Hikari.WindowsClient.Tests;

public class ContentItemViewModelTests
{
    private readonly SyncPreferencesRepository _prefs = new();

    [Fact]
    public void TickedButNotDownloadedShowsQueuedState()
    {
        var vm = NewViewModel();

        vm.IsMarked = true;

        Assert.True(vm.IsPending);
        Assert.Equal("\uE823", vm.SyncGlyph);
        Assert.Contains("not downloaded yet", vm.SyncTooltip);
    }

    [Fact]
    public void DownloadedItemIsNeverPending()
    {
        var vm = NewViewModel();
        vm.IsMarked = true;

        vm.IsLocal = true;

        Assert.False(vm.IsPending);
        Assert.Equal("\uE753", vm.SyncGlyph);
    }

    [Fact]
    public void UntickingRaisesTheGlyphChange()
    {
        var vm = NewViewModel();
        vm.IsMarked = true;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsMarked = false;

        Assert.Contains(nameof(ContentItemViewModel.SyncGlyph), raised);
        Assert.Equal("\uE896", vm.SyncGlyph);
    }

    private ContentItemViewModel NewViewModel() =>
        new(new ContentItem { Id = Guid.NewGuid().ToString("N"), Title = "Track" }, new AudioPlugin(), _prefs, canManage: false);
}
