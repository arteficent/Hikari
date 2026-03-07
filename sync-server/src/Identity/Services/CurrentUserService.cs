using SyncServer.Identity.Models;

namespace SyncServer.Identity.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        public User? CurrentUser { get; set; }
    }
}
