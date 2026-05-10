namespace SyncServer.Configuration
{
    /// <summary>
    /// Object storage settings (S3 / Cloudflare R2 / MinIO / DigitalOcean Spaces / etc.).
    /// Used exclusively for blob storage of content binaries.
    /// </summary>
    public class ObjectStorageSettings
    {
        public string BucketName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Custom S3-compatible endpoint URL. Leave empty for standard AWS S3.
        /// Set to "https://&lt;account-id&gt;.r2.cloudflarestorage.com" for Cloudflare R2.
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Force path-style addressing (required by R2, MinIO).
        /// </summary>
        public bool ForcePathStyle { get; set; }
    }

    /// <summary>
    /// AWS DynamoDB settings — independent of object storage so DynamoDB can stay on AWS
    /// while blobs live on Cloudflare R2 (or any other S3-compatible provider).
    /// </summary>
    public class DynamoDbSettings
    {
        public string Region { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }

    public class JwtSettings
    {
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int DurationInHours { get; set; } = 12;
    }
}
