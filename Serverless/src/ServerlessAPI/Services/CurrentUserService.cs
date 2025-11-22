using ServerlessAPI.Entities;

namespace ServerlessAPI.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        public User? CurrentUser { get; set; }
    }
}
