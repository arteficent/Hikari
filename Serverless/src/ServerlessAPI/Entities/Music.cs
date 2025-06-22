using Lambda.Abstraction;
using System.ComponentModel.DataAnnotations;

namespace ServerlessAPI.Entities
{
    internal class Music
    {
        [Key]
        public Guid Id { get; set; }
        [Required]
        public string? Title { get; set; }
        [Required]
        public string? Artist { get; set; }
        [Required]      
        public string? Album { get; set; }
        public string? Description { get; set; }
        [Required]
        public string? Genre { get; set; }
        [Required]
        public string? ReleaseDate { get; set; }
        public string? Duration { get; set; }
        [Required]
        public int Bitrate { get; set; }
        [Required]
        public int SizeInBytes { get; set; }
        [Required]
        public Format Format { get; set; }
        [Required]
        public string? CoverPath { get; set; } // music/cover/{artist}/{album}/{title}.{format}
        [Required]
        public string? StoragePath { get; set; }  // music/song/{artist}/{album}/{title}.{format}
        public string? Lyrics { get; set; }
        public string? Publisher { get; set; }
        public string? Copyright { get; set; }
        [Required]
        public string? Language { get; set; }
        [Required]
        public string? CountryOfOrigin { get; set; }
        public string? ISRC { get; set; }
        public string? Producer { get; set; }
        public string? Label { get; set; }
        public bool ExplicitContent { get; set; }
        public string[]? Tags { get; set; }
    }
}