namespace ServerlessAPI.Abstraction
{

    public class DownloadResponse
    {
        public ServerlessAPI.Entities.Music? Metadata { get; set; }
        public string? SongBinary { get; set; }
    }
}
