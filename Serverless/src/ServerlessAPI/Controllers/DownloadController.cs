using Amazon.S3;
using Amazon.S3.Model;
using Lambda.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServerlessAPI.Abstraction;
using ServerlessAPI.Entities;
using ServerlessAPI.Repositories;
using System;
using System.Linq;

namespace Lambda.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DownloadController : ControllerBase
    {
        private readonly ILogger<DownloadController> _logger;
        private readonly IMusicRepository _musicRepository;
        private readonly IAmazonS3 _s3Client;
        private readonly IOptions<AmazonWebServicesConstants> _awsConstants;

        public DownloadController(ILogger<DownloadController> logger, IMusicRepository musicRepository, IAmazonS3 s3Client, IOptions<AmazonWebServicesConstants> awsConstants)
        {
            _logger = logger;
            _musicRepository = musicRepository;
            _s3Client = s3Client;
            _awsConstants = awsConstants;
        }

        [HttpGet("songs")]
        public async Task<IActionResult> Download(
            [FromQuery] int? limit,
            [FromQuery] string? genre,
            [FromQuery] string? album,
            [FromQuery] string? playlist,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? titlePrefix = null)
        {
            _logger.LogInformation($"Download request received: limit={limit}, genre={genre}, album={album}, playlist={playlist}, page={page}, pageSize={pageSize}, titlePrefix={titlePrefix}");

            Func<Music, bool> filter = m =>
                (string.IsNullOrEmpty(genre) || m.Genre == genre) &&
                (string.IsNullOrEmpty(album) || m.Album == album) &&
                (string.IsNullOrEmpty(playlist) || (m.Tags != null && m.Tags.Contains(playlist))) &&
                (string.IsNullOrEmpty(titlePrefix) || (m.Title != null && m.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase)));

            var songs = await _musicRepository.GetMusicAsync(limit ?? pageSize, filter);
            var paged = songs.Skip((page - 1) * pageSize).Take(pageSize);

            var result = new List<DownloadResponse>();
            foreach (var song in paged)
            {
                string? base64Binary = null;
                if (!string.IsNullOrEmpty(song.StoragePath))
                {
                    try
                    {
                        var getRequest = new GetObjectRequest
                        {
                            BucketName = _awsConstants.Value.BucketName,
                            Key = song.StoragePath
                        };
                        using var response = await _s3Client.GetObjectAsync(getRequest);
                        using var ms = new MemoryStream();
                        await response.ResponseStream.CopyToAsync(ms);
                        base64Binary = Convert.ToBase64String(ms.ToArray());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to fetch binary for song {song.Title}.");
                    }
                }
                result.Add(new DownloadResponse {
                    Metadata = song,
                    SongBinary = base64Binary
                });
            }

            return Ok(result);
        }
    }
}
