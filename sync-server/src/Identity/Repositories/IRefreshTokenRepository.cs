using SyncServer.Identity.Models;

namespace SyncServer.Identity.Repositories
{
    public interface IRefreshTokenRepository
    {
        /// <summary>Persist a new refresh token record (token is hashed before storage).</summary>
        Task StoreAsync(string token, string userId, DateTime expiresAt);

        /// <summary>Look up a token. Returns null if not found OR if expired.</summary>
        Task<RefreshTokenRecord?> GetAsync(string token);

        /// <summary>Delete a token by its plaintext value.</summary>
        Task DeleteAsync(string token);
    }
}
