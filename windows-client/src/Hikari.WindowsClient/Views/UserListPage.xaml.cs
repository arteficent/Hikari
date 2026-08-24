using System.Collections.ObjectModel;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Hikari.WindowsClient.Views;

/// <summary>Row model for the admin user list.</summary>
public sealed class UserRowViewModel : ObservableBase
{
    private List<string> _roles;

    public UserRowViewModel(UserProfile profile)
    {
        Profile = profile;
        _roles = profile.Roles is null ? ["User"] : [.. profile.Roles];
    }

    public UserProfile Profile { get; }

    public string Id => Profile.Id;

    public string Username => Profile.Username;

    public IReadOnlyList<string> Roles => _roles;

    public string RolesDisplay => _roles.Count == 0 ? "User" : string.Join(", ", _roles);

    public bool IsRoot => _roles.Any(r => string.Equals(r, "Root", StringComparison.OrdinalIgnoreCase));

    public bool IsAdmin => _roles.Any(r => string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The Root account is a singleton: its roles cannot change and it cannot be
    /// removed, so its action buttons are hidden entirely.
    /// </summary>
    public Visibility ActionsVisibility => IsRoot ? Visibility.Collapsed : Visibility.Visible;

    public string AdminToggleTooltip => IsAdmin ? "Demote to plain user" : "Promote to admin";

    public Brush AdminBrush => (Brush)Application.Current.Resources[
        IsAdmin ? "HikariPrimaryBrush" : "HikariTextSoftBrush"];

    public List<string> RolesAfterToggle() => IsAdmin ? ["User"] : ["User", "Admin"];

    public void ApplyRoles(IEnumerable<string> roles)
    {
        _roles = [.. roles];
        Profile.Roles = _roles;
        Raise(nameof(Roles));
        Raise(nameof(RolesDisplay));
        Raise(nameof(IsAdmin));
        Raise(nameof(AdminToggleTooltip));
        Raise(nameof(AdminBrush));
    }
}

/// <summary>Root-only user management. Mirrors <c>UserListScreen.kt</c>.</summary>
public sealed partial class UserListPage : HikariPage
{
    private readonly ObservableCollection<UserRowViewModel> _users = [];

    public UserListPage()
    {
        InitializeComponent();
        UsersList.ItemsSource = _users;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;

        try
        {
            var users = await AppServices.Api.ListUsersAsync(ServerDomain);

            _users.Clear();
            foreach (var user in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
            {
                _users.Add(new UserRowViewModel(user));
            }
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Failed to load users", ex);
            ToastError(ex.Message);
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e) => GoBack();

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnCreateUserClicked(object sender, RoutedEventArgs e) => Shell.Navigate(typeof(CreateUserPage));

    private async void OnToggleAdmin(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is not { } row) return;

        var newRoles = row.RolesAfterToggle();
        try
        {
            await AppServices.Api.SetUserRolesAsync(ServerDomain, row.Id, newRoles);
            row.ApplyRoles(newRoles);
            ToastSuccess($"{row.Username} is now {row.RolesDisplay}.");
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Role update failed", ex);
            ToastError($"Role update failed: {ex.Message}");
        }
    }

    private async void OnRemoveUser(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is not { } row) return;

        if (!await ConfirmAsync(
                "Remove user?",
                $"This permanently removes {row.Username} from the system.",
                "Remove",
                destructive: true))
        {
            return;
        }

        try
        {
            await AppServices.Api.DeleteUserAsync(ServerDomain, row.Id);
            _users.Remove(row);
            ToastSuccess($"Removed {row.Username}.");
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("User delete failed", ex);
            ToastError($"Delete failed: {ex.Message}");
        }
    }

    private static UserRowViewModel? Model(object sender) =>
        (sender as FrameworkElement)?.DataContext as UserRowViewModel;
}
