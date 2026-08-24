using System.Text.RegularExpressions;
using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Hikari.WindowsClient.Views;

public sealed record DetailRow(string Label, string Value);

/// <summary>
/// One row in the content list. Wraps a <see cref="ContentItem"/> with the
/// presentation state the android client keeps inside <c>ContentItemCard</c>:
/// marked-for-sync, present-on-disk, cover art and the expandable detail rows.
/// </summary>
public sealed class ContentItemViewModel : ObservableBase
{
    private readonly IContentPlugin _plugin;
    private readonly SyncPreferencesRepository _syncPreferences;
    private bool _isMarked;
    private bool _isLocal;
    private bool _showDetails;
    private ImageSource? _cover;
    private bool _coverLoaded;

    public ContentItemViewModel(ContentItem item, IContentPlugin plugin, SyncPreferencesRepository syncPreferences, bool canManage)
    {
        Item = item;
        _plugin = plugin;
        _syncPreferences = syncPreferences;
        CanManage = canManage;
        _isMarked = syncPreferences.IsMarked(item.Id);
        _isLocal = syncPreferences.LocalPathFor(item.Id) is not null;
    }

    public ContentItem Item { get; }

    /// <summary>Admins and root see the edit and delete affordances; plain users do not.</summary>
    public bool CanManage { get; }

    public Visibility ManageVisibility => CanManage ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowDetails
    {
        get => _showDetails;
        set
        {
            if (Set(ref _showDetails, value)) Raise(nameof(DetailsVisibility));
        }
    }

    public Visibility DetailsVisibility => ShowDetails ? Visibility.Visible : Visibility.Collapsed;

    public string Id => Item.Id;

    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? "(untitled)" : Item.Title;

    public string Secondary => _plugin.SecondaryLine(Item);

    public string Glyph => _plugin.Glyph;

    /// <summary>
    /// Marked for sync. Writing this persists immediately — including when it is
    /// turned <i>off</i>, which is what lets a later Sync remove the file.
    /// </summary>
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (!Set(ref _isMarked, value)) return;

            _syncPreferences.SetSyncEnabled(Id, value);
            if (value) _syncPreferences.SetSyncEntry(Id, _plugin.RelativePathFor(Item));
        }
    }

    /// <summary>Whether the binary is currently on disk.</summary>
    public bool IsLocal
    {
        get => _isLocal;
        set
        {
            if (!Set(ref _isLocal, value)) return;
            Raise(nameof(SyncGlyph));
            Raise(nameof(SyncTooltip));
        }
    }

    public string SyncGlyph => IsLocal ? "\uE753" : "\uE896";

    public string SyncTooltip => IsLocal ? "Downloaded — click to remove from this PC" : "Not downloaded — click to download now";

    public ImageSource? Cover
    {
        get => _cover;
        private set => Set(ref _cover, value);
    }

    public IReadOnlyList<DetailRow> Details => BuildDetails();

    public void RefreshFromStore()
    {
        var marked = _syncPreferences.IsMarked(Id);
        if (marked != _isMarked)
        {
            _isMarked = marked;
            Raise(nameof(IsMarked));
        }

        IsLocal = _syncPreferences.LocalPathFor(Id) is not null;
    }

    /// <summary>
    /// Pulls embedded artwork out of the local file. Only meaningful once the item
    /// has been downloaded, so it is retried whenever <see cref="IsLocal"/> flips.
    /// </summary>
    public async Task LoadCoverAsync(string libraryRoot)
    {
        if (_coverLoaded || !IsLocal) return;
        _coverLoaded = true;

        try
        {
            var bytes = await Task.Run(() => _plugin.ExtractCoverArt(libraryRoot, Item));
            if (bytes is null || bytes.Length == 0) return;

            Cover = await ImageTools.FromBytesAsync(bytes);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Cover art unavailable for {Id}: {ex.Message}");
        }
    }

    public void InvalidateCover()
    {
        _coverLoaded = false;
        Cover = null;
    }

    /// <summary>True when the item matches the case-insensitive regex the user typed.</summary>
    public bool Matches(Regex? filter)
    {
        if (filter is null) return true;

        var haystack = string.Join(
            ' ',
            new[]
            {
                Item.Title,
                Item.Description,
                Item.Format,
                Item.Tags is null ? null : string.Join(' ', Item.Tags),
                Item.Metadata is null ? null : string.Join(' ', Item.Metadata.Values),
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return filter.IsMatch(haystack);
    }

    private List<DetailRow> BuildDetails()
    {
        var rows = new List<DetailRow>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(new DetailRow(label, value!));
        }

        Add("Description", Item.Description);
        Add("Format", Item.Format);
        if (Item.SizeInBytes > 0) Add("Size", FormatSize(Item.SizeInBytes));
        Add("Modified", Item.LastModified);
        Add("Created", Item.CreatedAt);
        if (Item.Tags is { Count: > 0 }) Add("Tags", string.Join(", ", Item.Tags));

        if (Item.Metadata is not null)
        {
            foreach (var (key, value) in Item.Metadata)
            {
                Add(Humanize(key), value);
            }
        }

        return rows;
    }

    private static string Humanize(string key)
    {
        var spaced = Regex.Replace(key, "([a-z0-9])([A-Z])", "$1 $2");
        return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    public static string FormatSize(long bytes)
    {
        double kb = bytes / 1024.0, mb = kb / 1024.0, gb = mb / 1024.0;
        if (gb >= 1) return $"{gb:F1} GB";
        if (mb >= 1) return $"{mb:F1} MB";
        if (kb >= 1) return $"{kb:F1} KB";
        return $"{bytes} B";
    }
}

public static class ImageTools
{
    /// <summary>Decodes raw image bytes into a XAML-ready bitmap.</summary>
    public static async Task<ImageSource> FromBytesAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
