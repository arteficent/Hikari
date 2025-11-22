using ServerlessAPI.Entities;

namespace ServerlessAPI.Services
{
    public interface ICurrentUserService
    {
        User? CurrentUser { get; set; }
    }
}
