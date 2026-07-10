using System.Security.Cryptography;
using System.Text;

namespace SyncServer.Identity.Security
{
    /// <summary>
    /// Hashes refresh tokens with SHA-256 (base64-url encoded) before persistence, so a
    /// leaked database snapshot never yields usable tokens. Shared by every
    /// <c>IRefreshTokenRepository</c> implementation so lookups match across backends.
    /// </summary>
    public static class RefreshTokenHasher
    {
        public static string Hash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
