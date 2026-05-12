using SyncServer.Identity.Models;

namespace SyncServer.Identity.Repositories
{
    public interface IUserRepository
    {
        Task<User> CreateAsync(User user, string plainPassword);
        Task<User?> GetByIdAsync(string id);
        Task<User?> GetByUsernameAsync(string username);
        Task<bool> UpdateAsync(User user);
        Task<bool> DeleteAsync(string id);
        Task<bool> ChangePasswordAsync(string id, string newPlainPassword);
        Task<IList<User>> ScanAllAsync();
    }
}
