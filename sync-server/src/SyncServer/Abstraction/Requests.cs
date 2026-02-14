using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using SyncServer.Abstraction;
using SyncServer.Entities;

namespace SyncServer.Abstraction
{
    public class CreateUserRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = null!;
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
        public string NewPassword { get; set; } = null!;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = null!;
    }

    public class UploadRequest
    {
        public Music? Metadata { get; set; }
        public string? SongBinary { get; set; }
    }

    public class DeleteRequest
    {
        public List<Music> Items { get; set; } = new();
    }
}
