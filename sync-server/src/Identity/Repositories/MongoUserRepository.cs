using MongoDB.Driver;
using SyncServer.Identity.Models;
using SyncServer.Identity.Security;

namespace SyncServer.Identity.Repositories
{
    /// <summary>
    /// MongoDB-backed <see cref="IUserRepository"/>. Collection name "Users" mirrors the
    /// DynamoDB table. A unique index on <c>Username</c> backs <see cref="GetByUsernameAsync"/>
    /// (the DynamoDB equivalent of the <c>username-index</c> GSI). Selected when
    /// <c>Database:Provider</c> is "MongoDb".
    /// </summary>
    public class MongoUserRepository : IUserRepository
    {
        private const string CollectionName = "Users";

        private readonly IMongoCollection<User> _users;
        private readonly ILogger<MongoUserRepository> _logger;
        private static int _indexEnsured;

        public MongoUserRepository(IMongoDatabase database, ILogger<MongoUserRepository> logger)
        {
            _users = database.GetCollection<User>(CollectionName);
            _logger = logger;
            EnsureUsernameIndex();
        }

        private void EnsureUsernameIndex()
        {
            // Create the unique username index once per process; best-effort.
            if (Interlocked.Exchange(ref _indexEnsured, 1) == 1) return;
            try
            {
                var model = new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Username),
                    new CreateIndexOptions { Unique = true, Name = "username-index" });
                _users.Indexes.CreateOne(model);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to ensure username index on Users collection");
            }
        }

        public async Task<User> CreateAsync(User user, string plainPassword)
        {
            if (user.Id == string.Empty)
                user.Id = Guid.NewGuid().ToString();

            user.CreatedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            user.PasswordHash = PasswordHasher.Hash(plainPassword);

            await _users.InsertOneAsync(user);
            return user;
        }

        public async Task<User?> GetByIdAsync(string id)
        {
            try
            {
                return await _users.Find(Builders<User>.Filter.Eq(u => u.Id, id)).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user {UserId}", id);
                return null;
            }
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            try
            {
                return await _users.Find(Builders<User>.Filter.Eq(u => u.Username, username)).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query user by username {Username}", username);
                return null;
            }
        }

        public async Task<IList<User>> ScanAllAsync()
        {
            try
            {
                return await _users.Find(Builders<User>.Filter.Empty).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list users");
                return new List<User>();
            }
        }

        public async Task<bool> UpdateAsync(User user)
        {
            try
            {
                user.UpdatedAt = DateTime.UtcNow;
                await _users.ReplaceOneAsync(Builders<User>.Filter.Eq(u => u.Id, user.Id), user, new ReplaceOptions { IsUpsert = true });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update user {UserId}", user.Id);
                return false;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                await _users.DeleteOneAsync(Builders<User>.Filter.Eq(u => u.Id, id));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete user {UserId}", id);
                return false;
            }
        }

        public async Task<bool> ChangePasswordAsync(string id, string newPlainPassword)
        {
            var user = await GetByIdAsync(id);
            if (user == null) return false;
            user.PasswordHash = PasswordHasher.Hash(newPlainPassword);
            user.UpdatedAt = DateTime.UtcNow;
            return await UpdateAsync(user);
        }
    }
}
