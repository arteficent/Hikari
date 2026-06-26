using System.Security.Cryptography;
using System.Text;

namespace SyncServer.Identity.Security
{
    /// <summary>
    /// Salted PBKDF2 (SHA-256, 100k iterations) password hashing shared by every
    /// <c>IUserRepository</c> implementation so DynamoDB- and MongoDB-backed stores
    /// produce and verify byte-identical hashes. Stored format: "{base64 salt}:{base64 hash}".
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static string Hash(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var hash = Convert.FromBase64String(parts[1]);
            var computed = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, hash.Length);
            return CryptographicOperations.FixedTimeEquals(hash, computed);
        }
    }
}
