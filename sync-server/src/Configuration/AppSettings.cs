namespace SyncServer.Configuration
{
    /// <summary>
    /// Object storage settings (S3 / Cloudflare R2 / MinIO / DigitalOcean Spaces / etc.).
    /// Used exclusively for blob storage of content binaries.
    /// </summary>
    public class ObjectStorageSettings
    {
        /// <summary>
        /// Selects the blob storage implementation. Supported values (case-insensitive):
        ///   "S3"    — <c>S3BlobStorageProvider</c> (AWS S3 and any S3-compatible API, incl. R2/MinIO).
        ///   "Minio" — <c>MinioBlobStorageProvider</c> (native MinIO SDK).
        /// Defaults to "S3" so existing deployments keep working unchanged.
        /// </summary>
        public string Provider { get; set; } = "S3";

        public string BucketName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Custom S3-compatible endpoint URL. Leave empty for standard AWS S3.
        /// Set to "https://&lt;account-id&gt;.r2.cloudflarestorage.com" for Cloudflare R2,
        /// or "http://minio:9000" for a MinIO server.
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional public-facing endpoint used <em>only</em> when signing presigned URLs.
        /// Presigned URLs embed the signing host, so when the server reaches the object
        /// store over an internal hostname (e.g. <c>http://minio:9000</c> inside Docker)
        /// but clients must reach it over a different one (e.g. <c>http://localhost:9000</c>
        /// or a LAN IP), set this to the client-reachable URL. When empty, presigning
        /// falls back to <see cref="ServiceUrl"/>. Currently honoured by the MinIO provider.
        /// </summary>
        public string PublicServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Force path-style addressing (required by R2, MinIO).
        /// </summary>
        public bool ForcePathStyle { get; set; }
    }

    /// <summary>
    /// Selects which database backend persists metadata (users, refresh tokens, content items).
    /// Storage of binaries is configured independently via <see cref="ObjectStorageSettings"/>.
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>
        /// Supported values (case-insensitive):
        ///   "DynamoDb" — AWS DynamoDB-backed repositories (default; unchanged behaviour).
        ///   "MongoDb"  — MongoDB-backed repositories.
        /// </summary>
        public string Provider { get; set; } = "DynamoDb";
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

    /// <summary>
    /// MongoDB settings. Used when <see cref="DatabaseSettings.Provider"/> is "MongoDb".
    /// Collection names mirror the DynamoDB table names ("Users", "RefreshTokens", and each
    /// plugin's <c>TableName</c>) so the two backends are schema-compatible.
    /// </summary>
    public class MongoDbSettings
    {
        /// <summary>Standard MongoDB connection string, e.g. "mongodb://user:pass@mongo:27017".</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>Database name that holds the Hikari collections.</summary>
        public string DatabaseName { get; set; } = "hikari";
    }

    public class JwtSettings
    {
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int DurationInHours { get; set; } = 12;
    }

    /// <summary>
    /// Bootstrap administrator credentials. Used to solve the chicken-and-egg
    /// problem on a fresh install: there are no users in the DB yet, so no one
    /// can authenticate to create the first admin.
    ///
    /// Behaviour at <c>POST /Auth/login</c>:
    ///   1. The DB is consulted first. If a user row exists for the given username,
    ///      authentication ALWAYS goes through the stored password hash and the
    ///      bootstrap credentials below are ignored.
    ///   2. If — and only if — no DB row exists for the bootstrap username, the
    ///      submitted password is compared against the value here. On success
    ///      the user is persisted to DynamoDB with role <c>Root</c>, after
    ///      which step (1) takes over forever.
    ///
    /// In other words, these are seed credentials, not a permanent backdoor.
    /// </summary>
    public class BootstrapAdminSettings
    {
        public string Username { get; set; } = "root";
        public string Password { get; set; } = "Root123!";
    }

    /// <summary>
    /// Hosting / reverse-proxy related settings. When the server runs behind a
    /// TLS-terminating proxy (Google Cloud Run, an L7 load balancer, an ingress
    /// controller, etc.) the inbound request reaches the container as plain
    /// HTTP and <c>UseHttpsRedirection</c> would either no-op or, worse, issue
    /// redirects that loop. Toggling <see cref="DisableHttpsRedirect"/> opts the
    /// pipeline out of HTTPS redirection so the same image can be deployed in
    /// both proxied and non-proxied environments.
    /// </summary>
    public class HostingSettings
    {
        public bool DisableHttpsRedirect { get; set; }
    }
}
