namespace SyncServer.Configuration
{
    /// <summary>
    /// Cloud-agnostic storage settings.
    /// Works for AWS S3, Cloudflare R2, MinIO, DigitalOcean Spaces, etc.
    /// </summary>
    public class CloudStorageSettings
    {
        public string BucketName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Custom S3-compatible endpoint URL. Leave empty for standard AWS S3.
        /// Set to "https://&lt;account-id&gt;.r2.cloudflarestorage.com" for R2, etc.
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Force path-style addressing (required by R2, MinIO).
        /// </summary>
        public bool ForcePathStyle { get; set; }
    }

    public class JwtSettings
    {
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int DurationInHours { get; set; } = 12;
    }
}
