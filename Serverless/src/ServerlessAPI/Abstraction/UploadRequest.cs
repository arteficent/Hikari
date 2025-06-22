using ServerlessAPI.Entities;
using System.ComponentModel.DataAnnotations;

namespace Lambda.Abstraction
{
    internal class UploadRequest
    {
        [Required]
        public string? Binary { get; set; }
        [Required]
        public Music? Metadata { get; set; }
    }
}