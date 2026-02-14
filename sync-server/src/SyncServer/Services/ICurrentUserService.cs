using SyncServer.Entities;

namespace SyncServer.Services
{
    public interface ICurrentUserService
    {
        User? CurrentUser { get; set; }
    }
}
