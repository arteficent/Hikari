using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using SyncServer.Identity.Models;

namespace SyncServer.Identity.Dtos
{
    public class CreateUserRequest
    {
        [Required]
        public string Username { get; set; } = null!;
        [Required]
        [PasswordPropertyText]
        public string Password { get; set; } = null!;
        [Required]
        public List<Role> Roles { get; set; } = [Role.User];
    }

    public class UpdateUserRequest
    {
        public User? Metadata { get; set; }
    }

    public class ChangePasswordRequest
    {
        [Required]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string NewPassword { get; set; } = null!;
    }

    public class UpdateUsernameRequest
    {
        [Required]
        public string Username { get; set; } = null!;
    }

    public class LoginRequest
    {
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = null!;
    }
}
