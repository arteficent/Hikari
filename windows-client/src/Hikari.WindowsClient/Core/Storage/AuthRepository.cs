using System.Security.Cryptography;
using System.Text;

namespace Hikari.WindowsClient.Core.Storage;

/// <summary>
/// Stores the access + refresh token pair. Mirrors
/// <c>android-client/app/src/core/storage/AuthRepository.kt</c>.
/// Unlike android, a desktop app has no per-app sandbox, so both tokens are
/// encrypted at rest with DPAPI scoped to the current Windows user.
/// </summary>
public sealed class AuthRepository : JsonPreferenceStore<AuthRepository.AuthState>
{
    public sealed class AuthState
    {
        public string? ProtectedToken { get; set; }
        public string? ProtectedRefreshToken { get; set; }
    }

    public AuthRepository() : base("auth.json") { }

    public string? Token => Unprotect(Read(s => s.ProtectedToken));

    public string? RefreshToken => Unprotect(Read(s => s.ProtectedRefreshToken));

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    public void SaveTokens(string token, string refreshToken)
    {
        AppLog.Debug("AuthRepository.SaveTokens()");
        Mutate(s =>
        {
            s.ProtectedToken = Protect(token);
            s.ProtectedRefreshToken = Protect(refreshToken);
        });
    }

    public void ClearTokens()
    {
        AppLog.Debug("AuthRepository.ClearTokens()");
        Mutate(s =>
        {
            s.ProtectedToken = null;
            s.ProtectedRefreshToken = null;
        });
    }

    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DPAPI protect failed, storing token in the clear: {ex.Message}");
            return value;
        }
    }

    private static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(stored), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Either an unprotected legacy value or a token written by a different
            // Windows user profile. Fall back to the raw string; the server will
            // reject it and the normal re-login flow takes over.
            return stored;
        }
    }
}
