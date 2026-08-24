namespace Hikari.WindowsClient.Core.Network;

/// <summary>
/// User profile returned by GET /User/me and the admin user-list endpoints.
/// Roles are serialised by the server as strings ("User", "Admin", "Root").
/// </summary>
public sealed class UserProfile
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<string>? Roles { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }

    public string RolesDisplay => Roles is null || Roles.Count == 0 ? "User" : string.Join(", ", Roles);
}

public sealed class ChangeUsernameRequest
{
    public string Username { get; set; } = string.Empty;

    public ChangeUsernameRequest() { }

    public ChangeUsernameRequest(string username) => Username = username;
}

public sealed class ChangePasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;

    public ChangePasswordRequest() { }

    public ChangePasswordRequest(string newPassword) => NewPassword = newPassword;
}

/// <summary>
/// Admin-only user creation. Roles use the server's enum-string values
/// ("User", "Admin"); the server rejects "Root", and Admin callers may only
/// create plain <c>User</c> accounts.
/// </summary>
public sealed class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();

    public CreateUserRequest() { }

    public CreateUserRequest(string username, string password, List<string> roles)
    {
        Username = username;
        Password = password;
        Roles = roles;
    }
}
