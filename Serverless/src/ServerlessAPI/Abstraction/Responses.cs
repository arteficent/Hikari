namespace ServerAPI.Abstraction
{

    public class DownloadResponse
    {
        public ServerAPI.Entities.Music? Metadata { get; set; }
        public string? SongBinary { get; set; }
    }
}
