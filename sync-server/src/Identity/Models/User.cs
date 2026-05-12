using System.ComponentModel.DataAnnotations;
using Amazon.DynamoDBv2.DataModel;

namespace SyncServer.Identity.Models
{
    [DynamoDBTable("Users")]
    public class User
    {
        [DynamoDBHashKey]
        public string Id { get; set; } = string.Empty;

        [Required]
        [DynamoDBProperty]
        [DynamoDBGlobalSecondaryIndexHashKey("username-index")]
        public string Username { get; set; } = null!;

        // Stored as "{salt}:{hash}"
        [Required]
        [DynamoDBProperty]
        public string PasswordHash { get; set; } = null!;

        // Playlist of music Ids
        [DynamoDBProperty]
        public List<Guid>? Playlist { get; set; }

        // Roles for authorization (e.g. "Admin","User")
        [DynamoDBProperty]
        public List<Role>? Roles { get; set; }

        [DynamoDBProperty]
        public DateTime CreatedAt { get; set; }

        [DynamoDBProperty]
        public DateTime UpdatedAt { get; set; }
    }
}
