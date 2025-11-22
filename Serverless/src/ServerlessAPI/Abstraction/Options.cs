namespace ServerlessAPI.Abstraction
{
    public class AmazonWebServicesConstants
    {
        public string BucketName { get; set; } = string.Empty;
        public string SongTableName { get; set; } = string.Empty;
        public string UserTableName { get; set; } = string.Empty;
        public string AwsRegion { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }

    public class JwtConstants
    {
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int DurationInHours { get; set; } = 12;
    }
}
