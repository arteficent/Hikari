using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using ServerlessAPI.Entities;
using System.Security.Cryptography;
using System.Text;

namespace ServerlessAPI.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDynamoDBContext context;
        private readonly ILogger<UserRepository> logger;

        public UserRepository(IDynamoDBContext context, ILogger<UserRepository> logger)
        {
            this.context = context;
            this.logger = logger;
        }

        public async Task<User> CreateAsync(User user, string plainPassword)
        {
            // Only generate a new Id if one wasn't provided by the caller
            if (user.Id == string.Empty)
                user.Id = Guid.NewGuid().ToString();

            user.CreatedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            user.PasswordHash = HashPassword(plainPassword);

            await context.SaveAsync(user);
            return user;
        }

        public async Task<User?> GetByIdAsync(string id)
        {
            try
            {
                return await context.LoadAsync<User>(id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load user {UserId}", id);
                return null;
            }
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            try
            {
                var config = new QueryOperationConfig
                {
                    IndexName = "email-index", // Changed from "email-index"
                    KeyExpression = new Expression
                    {
                        ExpressionStatement = "Email = :v_Email",
                        ExpressionAttributeValues = new Dictionary<string, DynamoDBEntry>
                {
                    { ":v_Email", email }
                }
                    },
                    Limit = 1
                };
                var query = context.FromQueryAsync<User>(config);
                var results = await query.GetRemainingAsync();
                return results.FirstOrDefault();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to query user by email {Email}", email);
                throw; // Or return null, depending on your error handling strategy
            }
        }

        public async Task<IList<User>> ScanAllAsync()
        {
            try
            {
                var scan = context.ScanAsync<User>(new List<ScanCondition>());
                var all = await scan.GetRemainingAsync();
                return all;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scan users");
                return new List<User>();
            }
        }

        public async Task<bool> UpdateAsync(User user)
        {
            try
            {
                user.UpdatedAt = DateTime.UtcNow;
                await context.SaveAsync(user);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update user {UserId}", user.Id);
                return false;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                await context.DeleteAsync<User>(id);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete user {UserId}", id);
                return false;
            }
        }

        public async Task<bool> ChangePasswordAsync(string id, string newPlainPassword)
        {
            var user = await GetByIdAsync(id);
            if (user == null) return false;
            user.PasswordHash = HashPassword(newPlainPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveAsync(user);
            return true;
        }

        // Simple salted PBKDF2 password hashing
        private static string HashPassword(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[16];
            rng.GetBytes(salt);
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var hash = Convert.FromBase64String(parts[1]);
            var computed = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, hash.Length);
            return CryptographicOperations.FixedTimeEquals(hash, computed);
        }
    }
}
