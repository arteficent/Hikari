using MongoDB.Driver;
using SyncServer.Identity.Models;
using SyncServer.Identity.Security;

namespace SyncServer.Identity.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IRefreshTokenRepository"/>. Only the SHA-256 hash of each
    /// token is stored, as the document <c>_id</c> (see <c>MongoMappings</c>). A TTL index on
    /// <c>ExpiresAt</c> auto-evicts expired records, mirroring the DynamoDB TTL attribute.
    /// Collection name "RefreshTokens" mirrors the DynamoDB table.
    /// Selected when <c>Database:Provider</c> is "MongoDb".
    /// </summary>
    public class MongoRefreshTokenRepository : IRefreshTokenRepository
    {
        private const string CollectionName = "RefreshTokens";

        private readonly IMongoCollection<RefreshTokenRecord> _tokens;
        private readonly ILogger<MongoRefreshTokenRepository> _logger;
        private static int _indexEnsured;

        public MongoRefreshTokenRepository(IMongoDatabase database, ILogger<MongoRefreshTokenRepository> logger)
        {
            _tokens = database.GetCollection<RefreshTokenRecord>(CollectionName);
            _logger = logger;
            EnsureTtlIndex();
        }

        private void EnsureTtlIndex()
        {
            if (Interlocked.Exchange(ref _indexEnsured, 1) == 1) return;
            try
            {
                var model = new CreateIndexModel<RefreshTokenRecord>(
                    Builders<RefreshTokenRecord>.IndexKeys.Ascending(r => r.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "expiresAt-ttl" });
                _tokens.Indexes.CreateOne(model);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to ensure TTL index on RefreshTokens collection");
            }
        }

        private static FilterDefinition<RefreshTokenRecord> ByHash(string token) =>
            Builders<RefreshTokenRecord>.Filter.Eq(r => r.TokenHash, RefreshTokenHasher.Hash(token));

        public async Task StoreAsync(string token, string userId, DateTime expiresAt)
        {
            var record = new RefreshTokenRecord
            {
                TokenHash = RefreshTokenHasher.Hash(token),
                UserId = userId,
                ExpiresAt = expiresAt,
                ExpiresAtEpoch = new DateTimeOffset(expiresAt, TimeSpan.Zero).ToUnixTimeSeconds(),
                CreatedAt = DateTime.UtcNow
            };

            await _tokens.ReplaceOneAsync(
                Builders<RefreshTokenRecord>.Filter.Eq(r => r.TokenHash, record.TokenHash),
                record,
                new ReplaceOptions { IsUpsert = true });
        }

        public async Task<RefreshTokenRecord?> GetAsync(string token)
        {
            try
            {
                var record = await _tokens.Find(ByHash(token)).FirstOrDefaultAsync();
                if (record == null) return null;
                if (record.ExpiresAt < DateTime.UtcNow)
                {
                    // Best-effort cleanup; the TTL index will eventually catch the rest.
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
                await _tokens.DeleteOneAsync(ByHash(token));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete refresh token");
            }
        }
    }
}
