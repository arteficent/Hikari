using SyncServer.Entities;

namespace SyncServer.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        public User? CurrentUser { get; set; }
    }
}
