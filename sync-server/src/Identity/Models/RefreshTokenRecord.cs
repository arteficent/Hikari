using Amazon.DynamoDBv2.DataModel;

namespace SyncServer.Identity.Models
{
    /// <summary>
    /// Persisted refresh token record. Only the SHA-256 hash of the token is stored,
    /// so a database compromise does not yield usable refresh tokens.
    /// </summary>
    [DynamoDBTable("RefreshTokens")]
    public class RefreshTokenRecord
    {
        /// <summary>SHA-256 hash of the refresh token (base64-url encoded).</summary>
        [DynamoDBHashKey]
        public string TokenHash { get; set; } = string.Empty;

        [DynamoDBProperty]
        public string UserId { get; set; } = string.Empty;

        /// <summary>UTC expiry timestamp.</summary>
        [DynamoDBProperty]
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Unix epoch seconds, mirrored from <see cref="ExpiresAt"/>. Set this attribute
        /// as the table's TTL field in DynamoDB so expired tokens are auto-evicted.
        /// </summary>
        [DynamoDBProperty]
        public long ExpiresAtEpoch { get; set; }

        [DynamoDBProperty]
        public DateTime CreatedAt { get; set; }
    }
}
