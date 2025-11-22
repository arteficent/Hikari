using Lambda.Abstraction;
using System.ComponentModel.DataAnnotations;
using Amazon.DynamoDBv2.DataModel;

namespace ServerlessAPI.Entities
{
    [DynamoDBTable("Music")]
    public class Music
    {
        [DynamoDBHashKey]
        public Guid Id { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? Title { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? Artist { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? Album { get; set; }

        [DynamoDBProperty]
        public string? Description { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? Genre { get; set; }

        [Required]
        [DynamoDBProperty]
        public DateTime? ReleaseDate { get; set; }

        [Required]
        [DynamoDBProperty]
        public DateTime? LastModified { get; set; }

        [DynamoDBProperty]
        public string? Duration { get; set; }

        [Required]
        [DynamoDBProperty]
        public int Bitrate { get; set; }

        [Required]
        [DynamoDBProperty]
        public int SizeInBytes { get; set; }

        [Required]
        [DynamoDBProperty]
        public ContentType MusicFormat { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? StoragePath { get; set; }  // music/song/{artist}/{album}/{title}.{format}

        [DynamoDBProperty]
        public string? Lyrics { get; set; }

        [DynamoDBProperty]
        public string? Publisher { get; set; }

        [DynamoDBProperty]
        public string? Copyright { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? Language { get; set; }

        [Required]
        [DynamoDBProperty]
        public string? CountryOfOrigin { get; set; }

        [DynamoDBProperty]
        public string? ISRC { get; set; }

        [DynamoDBProperty]
        public string? Producer { get; set; }

        [DynamoDBProperty]
        public string? Label { get; set; }

        [DynamoDBProperty]
        public bool ExplicitContent { get; set; }

        [DynamoDBProperty]
        public string[]? Tags { get; set; }
    }
}