using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Hikari.WindowsClient.Views;

/// <summary>Navigation parameter: which plugin, and whether we're editing an existing item.</summary>
public sealed record UploadArgs(IContentPlugin Plugin, ContentItem? EditingItem);

/// <summary>
/// Generic upload / edit screen. Mirrors <c>UploadScreen.kt</c>, including its
/// three-step contract with the sync-server:
///   1. POST /content/{type}/upload-init      → presigned URL
///   2. PUT  the binary to that URL           → direct object-storage upload
///   3. POST /content/{type}/upload-complete  → finalise metadata
/// </summary>
public sealed partial class UploadPage : HikariPage
{
    private IContentPlugin _plugin = null!;
    private ContentItem? _editingItem;
    private string? _selectedFilePath;
    private string? _coverImagePath;
    private bool _busy;

    public UploadPage()
    {
        InitializeComponent();
    }

    private bool IsEditing => _editingItem is not null;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not UploadArgs args)
        {
            GoBack();
            return;
        }

        _plugin = args.Plugin;
        _editingItem = args.EditingItem;

        TitleLabel.Text = IsEditing ? $"Edit {_plugin.DisplayName}" : $"Upload {_plugin.DisplayName}";
        SubmitLabel.Text = IsEditing ? "Save changes" : "Upload";
        PickFileLabel.Text = IsEditing ? "Replace file" : "Pick file";
        PluginFieldsLabel.Text = _plugin.DisplayName.ToUpperInvariant();
        EditHintLabel.Visibility = IsEditing ? Visibility.Visible : Visibility.Collapsed;

        PluginForm.Render(_plugin.UploadFields);
        PluginFieldsCard.Visibility = _plugin.UploadFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        CoverPanel.Visibility = _plugin.SupportsCoverImage ? Visibility.Visible : Visibility.Collapsed;
        PickCoverButton.Content = $"Replace {_plugin.CoverImageLabel}";
        RewriteMetadataCheck.Checked += (_, _) => UpdateCoverWarning();
        RewriteMetadataCheck.Unchecked += (_, _) => UpdateCoverWarning();

        if (_editingItem is not null)
        {
            TitleBox.Text = _editingItem.Title;
            DescriptionBox.Text = _editingItem.Description ?? string.Empty;
            TagsBox.Text = _editingItem.Tags is null ? string.Empty : string.Join(", ", _editingItem.Tags);
            PluginForm.SetValues(_editingItem.Metadata);
            FileNameLabel.Text = _editingItem.StoragePath?.Split('/').LastOrDefault() ?? "No file selected";
        }
        else
        {
            FileNameLabel.Text = "No file selected";
        }

        UpdateCoverWarning();
    }

    // ── File pickers ────────────────────────────────────────

    private async void OnPickFileClicked(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(_plugin.UploadFileExtensions);
        if (file is null) return;

        _selectedFilePath = file.Path;
        FileNameLabel.Text = file.Name;
        ClearStatus();

        // Best-effort metadata auto-fill; only empty fields are populated so the
        // user's own edits are never overwritten.
        try
        {
            var extracted = await Task.Run(() => _plugin.ExtractFileMetadata(_selectedFilePath));

            if (extracted.TryGetValue("title", out var extractedTitle) && string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = extractedTitle;
            }

            foreach (var (key, value) in extracted)
            {
                if (key != "title") PluginForm.FillIfEmpty(key, value);
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Metadata extraction failed for {file.Name}: {ex.Message}");
        }

        if (_plugin.SupportsCoverImage && _coverImagePath is null)
        {
            await ShowEmbeddedCoverAsync();
        }
    }

    private async Task ShowEmbeddedCoverAsync()
    {
        try
        {
            var bytes = await Task.Run(() => _plugin.ExtractCoverArtFromFile(_selectedFilePath!));
            if (bytes is { Length: > 0 })
            {
                CoverPreview.Source = await ImageTools.FromBytesAsync(bytes);
                CoverStatusLabel.Text = $"Existing {_plugin.CoverImageLabel.ToLowerInvariant()} (will be kept)";
            }
            else
            {
                CoverPreview.Source = null;
                CoverStatusLabel.Text = $"No {_plugin.CoverImageLabel.ToLowerInvariant()} embedded in this file";
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Cover extraction failed: {ex.Message}");
        }
    }

    private async void OnPickCoverClicked(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync([".png", ".jpg", ".jpeg", ".webp", ".bmp"]);
        if (file is null) return;

        _coverImagePath = file.Path;
        UndoCoverButton.Visibility = Visibility.Visible;
        CoverStatusLabel.Text = $"New {_plugin.CoverImageLabel.ToLowerInvariant()} (will replace the existing one)";

        try
        {
            CoverPreview.Source = await ImageTools.FromBytesAsync(await File.ReadAllBytesAsync(file.Path));
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Cover preview failed: {ex.Message}");
        }

        UpdateCoverWarning();
    }

    private async void OnUndoCoverClicked(object sender, RoutedEventArgs e)
    {
        _coverImagePath = null;
        UndoCoverButton.Visibility = Visibility.Collapsed;
        CoverPreview.Source = null;
        CoverStatusLabel.Text = string.Empty;
        UpdateCoverWarning();

        if (_selectedFilePath is not null) await ShowEmbeddedCoverAsync();
    }

    private void UpdateCoverWarning()
    {
        var needsRewrite = _coverImagePath is not null && RewriteMetadataCheck.IsChecked != true;
        CoverWarningLabel.Text =
            $"Enable \"Update metadata inside the file\" to embed the new {_plugin.CoverImageLabel.ToLowerInvariant()}.";
        CoverWarningLabel.Visibility = needsRewrite ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<StorageFile?> PickFileAsync(IReadOnlyList<string> extensions)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            if (extensions.Count == 0) picker.FileTypeFilter.Add("*");
            else foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);

            WinRT.Interop.InitializeWithWindow.Initialize(picker, Shell.Hwnd);
            return await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("File picker failed", ex);
            ToastError($"Could not open the file picker: {ex.Message}");
            return null;
        }
    }

    // ── Submit ──────────────────────────────────────────────

    private void OnBackClicked(object sender, RoutedEventArgs e) => GoBack();

    private async void OnSubmitClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var title = TitleBox.Text.Trim();
        var fields = PluginForm.GetValues();

        if (!IsEditing && _selectedFilePath is null) { ShowError("Please select a file."); return; }
        if (string.IsNullOrWhiteSpace(title)) { ShowError("Title is required."); return; }

        var validationError = _plugin.ValidateUploadFields(fields);
        if (validationError is not null) { ShowError(validationError); return; }

        SetBusy(true);
        try
        {
            if (IsEditing && _selectedFilePath is null) await SaveMetadataOnlyAsync(title, fields);
            else await UploadBinaryAsync(title, fields);
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Upload failed", ex);
            ShowError(ex is ApiStatusException api ? $"Server rejected the request ({(int)api.StatusCode}): {api.Message}" : ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveMetadataOnlyAsync(string title, Dictionary<string, string> fields)
    {
        var updated = _editingItem!.Clone();
        updated.Title = title;
        updated.Description = Blank(DescriptionBox.Text);
        updated.Tags = ParseTags();
        updated.Metadata = _plugin.BuildUploadMetadata(title, fields);
        updated.Format = _plugin.ResolveUploadFormat(fields, _editingItem.StoragePath ?? string.Empty) is { Length: > 0 } format
            ? format
            : _editingItem.Format;
        updated.LastModified = null;

        await AppServices.Api.EditContentAsync(ServerDomain, _plugin.ContentType, updated);

        ToastSuccess($"Metadata updated for '{title}'.");
        GoBack();
    }

    private async Task UploadBinaryAsync(string title, Dictionary<string, string> fields)
    {
        var sourcePath = _selectedFilePath!;
        UploadProgress.Visibility = Visibility.Visible;

        // Either rewrite the file's tags into a temp copy, or stream the original as-is.
        Stream payload = RewriteMetadataCheck.IsChecked == true
            ? await _plugin.RewriteFileMetadataAsync(sourcePath, title, fields, _coverImagePath)
            : File.OpenRead(sourcePath);

        try
        {
            var uploadItem = new ContentItem
            {
                Id = _editingItem?.Id ?? Guid.NewGuid().ToString(),
                ContentType = _plugin.ContentType,
                Title = title,
                Description = Blank(DescriptionBox.Text),
                Format = _plugin.ResolveUploadFormat(fields, sourcePath),
                SizeInBytes = payload.CanSeek ? payload.Length : 0,
                StoragePath = null,
                LastModified = null,
                CreatedAt = _editingItem?.CreatedAt,
                Tags = ParseTags(),
                Metadata = _plugin.BuildUploadMetadata(title, fields),
            };

            SetStatus("Requesting upload URL…", InfoBarSeverity.Informational);
            var init = await AppServices.Api.UploadInitAsync(
                ServerDomain, _plugin.ContentType, new ContentUploadInitRequest { Item = uploadItem });

            SetStatus("Uploading…", InfoBarSeverity.Informational);
            await AppServices.Api.UploadBinaryAsync(
                init.UploadUrl, payload, init.RequiredHeaders, _plugin.ResolveUploadMimeType(fields));

            SetStatus("Finalising…", InfoBarSeverity.Informational);
            var complete = await AppServices.Api.UploadCompleteAsync(
                ServerDomain, _plugin.ContentType, new ContentUploadCompleteRequest { Item = init.Item });

            await CleanUpMovedOriginalAsync(init, complete);

            if (IsEditing)
            {
                ToastSuccess($"Updated '{title}'.");
                GoBack();
            }
            else
            {
                ToastSuccess($"Upload complete: '{title}'.");
                ResetForm();
            }
        }
        finally
        {
            await payload.DisposeAsync();
            UploadProgress.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// When an edit changes the metadata that determines the storage path, the server
    /// writes a fresh row at the new path and the old one is orphaned. Remove it.
    /// </summary>
    private async Task CleanUpMovedOriginalAsync(ContentUploadInitResponse init, ContentUploadCompleteResponse complete)
    {
        if (_editingItem is null) return;

        var oldPath = _editingItem.StoragePath;
        var newPath = complete.Item?.StoragePath ?? init.Item.StoragePath;

        if (string.IsNullOrWhiteSpace(oldPath) ||
            string.IsNullOrWhiteSpace(newPath) ||
            string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await AppServices.Api.DeleteItemsAsync(ServerDomain, _plugin.ContentType, [_editingItem]);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: the edit itself succeeded, so don't fail it.
            AppLog.Warn($"Could not remove the orphaned item at '{oldPath}': {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    private List<string>? ParseTags()
    {
        var tags = TagsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return tags.Count > 0 ? tags : null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ResetForm()
    {
        _selectedFilePath = null;
        _coverImagePath = null;
        TitleBox.Text = string.Empty;
        DescriptionBox.Text = string.Empty;
        TagsBox.Text = string.Empty;
        PluginForm.Clear();
        RewriteMetadataCheck.IsChecked = false;
        CoverPreview.Source = null;
        CoverStatusLabel.Text = string.Empty;
        UndoCoverButton.Visibility = Visibility.Collapsed;
        FileNameLabel.Text = "No file selected";
        UpdateCoverWarning();
    }

    private void ShowError(string message) => SetStatus(message, InfoBarSeverity.Error);

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void ClearStatus() => StatusBar.IsOpen = false;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SubmitButton.IsEnabled = !busy;
        PickFileButton.IsEnabled = !busy;
        PickCoverButton.IsEnabled = !busy;
        SubmitRing.IsActive = busy;
        SubmitRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SubmitIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
    }
}
