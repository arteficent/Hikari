using Amazon.DynamoDBv2.DataModel;
using SyncServer.Identity.Models;
using System.Security.Cryptography;
using System.Text;

namespace SyncServer.Identity.Repositories
{
    /// <summary>
    /// DynamoDB-backed refresh token store. Tokens are stored as SHA-256 hashes
    /// so a leaked database snapshot doesn't yield usable tokens.
    ///
    /// Recommended table schema:
    ///   Table: RefreshTokens
    ///   Partition key: TokenHash (String)
    ///   Time-to-live attribute: ExpiresAtEpoch (Number, seconds since epoch)
    /// Configure DynamoDB TTL to auto-evict expired records.
    /// </summary>
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IDynamoDBContext _context;
        private readonly ILogger<RefreshTokenRepository> _logger;

        public RefreshTokenRepository(IDynamoDBContext context, ILogger<RefreshTokenRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task StoreAsync(string token, string userId, DateTime expiresAt)
        {
            var record = new RefreshTokenRecord
            {
                TokenHash = HashToken(token),
                UserId = userId,
                ExpiresAt = expiresAt,
                ExpiresAtEpoch = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToUnixTimeSeconds(),
                CreatedAt = DateTime.UtcNow
            };

            await _context.SaveAsync(record);
        }

        public async Task<RefreshTokenRecord?> GetAsync(string token)
        {
            try
            {
                var record = await _context.LoadAsync<RefreshTokenRecord>(HashToken(token));
                if (record == null) return null;
                if (record.ExpiresAt < DateTime.UtcNow)
                {
                    // Best-effort cleanup; DynamoDB TTL will eventually catch the rest.
                    _ = DeleteAsync(token);
                    return null;
                }
                return record;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load refresh token");
                return null;
            }
        }

        public async Task DeleteAsync(string token)
        {
            try
            {
                await _context.DeleteAsync<RefreshTokenRecord>(HashToken(token));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete refresh token");
            }
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            // Base64-url (DynamoDB hash key allows base64 standard chars too, but
            // url-safe avoids any ambiguity in logs and config).
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
