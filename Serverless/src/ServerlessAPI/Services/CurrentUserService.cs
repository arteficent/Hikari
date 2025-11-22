using ServerAPI.Entities;

namespace ServerAPI.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        public User? CurrentUser { get; set; }
    }
}
