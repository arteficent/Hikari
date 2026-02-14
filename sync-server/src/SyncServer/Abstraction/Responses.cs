namespace SyncServer.Abstraction
{

    public class DownloadResponse
    {
        public SyncServer.Entities.Music? Metadata { get; set; }
        public string? SongBinary { get; set; }
    }
}
