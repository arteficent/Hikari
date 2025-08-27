using ServerlessAPI.Entities;
using System.ComponentModel.DataAnnotations;

namespace Lambda.Abstraction
{
    public class UploadRequest
    {
        [Required]
        public string? SongBinary { get; set; }
        [Required]
        public Music? Metadata { get; set; }
    }
}