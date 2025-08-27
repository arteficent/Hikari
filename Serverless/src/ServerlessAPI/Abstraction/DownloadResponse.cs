using ServerlessAPI.Entities;

namespace ServerlessAPI.Abstraction
{
    public class DownloadResponse
    {
        public Music Metadata { get; set; } = default!;
        public string? SongBinary { get; set; }
    }
}
