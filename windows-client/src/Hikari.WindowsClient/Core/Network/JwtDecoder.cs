using System.Text;
using System.Text.Json;

namespace Hikari.WindowsClient.Core.Network;

/// <summary>
/// Lightweight JWT helpers — decode the payload locally so the UI can branch on
/// the current user's identity (id, username, roles) without an extra network
/// round trip. The signature is NOT verified here; the server remains the source
/// of truth for authorization. Decoded claims are advisory hints only.
/// Mirrors <c>android-client/app/src/core/network/JwtDecoder.kt</c>.
/// </summary>
public static class JwtDecoder
{
    public sealed record Claims(string? UserId, string? Username, IReadOnlyList<string> Roles)
    {
        public bool IsRoot => Roles.Any(r => string.Equals(r, "Root", StringComparison.OrdinalIgnoreCase));

        /// <summary>Root inherits all Admin powers, so this is true for Root too.</summary>
        public bool IsAdmin =>
            IsRoot || Roles.Any(r => string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase));
    }

    public static Claims? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new Claims(
                UserId: ReadString(root, "sub"),
                Username: ReadString(root, "username"),
                Roles: ExtractRoles(root));
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(el.GetString())
                ? el.GetString()
                : null;

    private static IReadOnlyList<string> ExtractRoles(JsonElement obj)
    {
        // ASP.NET emits role claims under the long URI; .NET also serialises them as a
        // JSON string when there is a single role, and an array when there are many.
        string[] keys =
        [
            "role",
            "roles",
            "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
        ];

        foreach (var key in keys)
        {
            if (!obj.TryGetProperty(key, out var value)) continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.Array:
                    return value.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                case JsonValueKind.String:
                    var single = value.GetString();
                    return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : [single];
                default:
                    return Array.Empty<string>();
            }
        }

        return Array.Empty<string>();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
