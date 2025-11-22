using ServerAPI.Entities;

namespace ServerAPI.Services
{
    public interface ICurrentUserService
    {
        User? CurrentUser { get; set; }
    }
}
