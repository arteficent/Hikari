using SyncServer.Identity.Models;

namespace SyncServer.Identity.Services
{
    public interface ICurrentUserService
    {
        User? CurrentUser { get; set; }
    }
}
