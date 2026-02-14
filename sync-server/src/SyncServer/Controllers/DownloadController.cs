using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SyncServer.Entities;
using SyncServer.Repositories;
using SyncServer.Abstraction;
using System.Globalization;

namespace SyncServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Roles = "User,Admin")]
    public class DownloadController : ControllerBase
    {
        private readonly ILogger<DownloadController> _logger;
        private readonly IMusicRepository _musicRepository;
        private readonly IAmazonS3 _s3Client;
        private readonly IOptions<SyncServer.Abstraction.AmazonWebServicesConstants> _awsConstants;

        public DownloadController(ILogger<DownloadController> logger, IMusicRepository musicRepository, IAmazonS3 s3Client, IOptions<SyncServer.Abstraction.AmazonWebServicesConstants> awsConstants)
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
            [FromQuery] string? artist,
            [FromQuery] string? playlist,
            [FromQuery] string? releaseFrom,
            [FromQuery] string? releaseTo,
            [FromQuery] string? lastModifiedSince,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? titlePrefix = null)
        {
            _logger.LogInformation("Download request received: limit={Limit}, genre={Genre}, album={Album}, artist={Artist}, playlist={Playlist}, releaseFrom={ReleaseFrom}, releaseTo={ReleaseTo}, lastModifiedSince={LastModifiedSince}, page={Page}, pageSize={PageSize}, titlePrefix={TitlePrefix}", limit, genre, album, artist, playlist, releaseFrom, releaseTo, lastModifiedSince, page, pageSize, titlePrefix);

            var releaseFromDate = ParseDate(releaseFrom);
            var releaseToDate = ParseDate(releaseTo);
            var lastModifiedSinceDate = ParseDate(lastModifiedSince);

            Func<Music, bool> filter = m =>
                (string.IsNullOrEmpty(genre) || m.Genre == genre) &&
                (string.IsNullOrEmpty(album) || m.Album == album) &&
                (string.IsNullOrEmpty(artist) || (m.Artist != null && m.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase))) &&
                (string.IsNullOrEmpty(playlist) || (m.Tags != null && m.Tags.Contains(playlist))) &&
                (string.IsNullOrEmpty(titlePrefix) || (m.Title != null && m.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase))) &&
                MatchesReleaseDate(m.ReleaseDate, releaseFromDate, releaseToDate) &&
                MatchesLastModified(m.LastModified, lastModifiedSinceDate);

            var songs = await _musicRepository.GetMusicAsync(limit ?? int.MaxValue, filter);
            var paged = songs.Skip((page - 1) * pageSize).Take(pageSize).ToList();

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
                        _logger.LogError(ex, "Failed to fetch binary for song {Title}.", song.Title);
                    }
                }
                result.Add(new DownloadResponse {
                    Metadata = song,
                    SongBinary = base64Binary
                });
            }

            return Ok(result);
        }

        [HttpGet("song")]
        public async Task<IActionResult> DownloadSong([FromQuery] string id)
        {
            if (!Guid.TryParse(id, out var songId))
                return BadRequest("Invalid id.");

            var song = await _musicRepository.GetByIdAsync(songId);
            if (song == null) return NotFound();

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
                    _logger.LogError(ex, "Failed to fetch binary for song {Title}.", song.Title);
                }
            }

            return Ok(new DownloadResponse
            {
                Metadata = song,
                SongBinary = base64Binary
            });
        }

        private static DateTimeOffset? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
            return null;
        }

        private static bool MatchesReleaseDate(DateTime? releaseDate, DateTimeOffset? from, DateTimeOffset? to)
        {
            if (from == null && to == null) return true;
            if (releaseDate == null) return false;
            var value = new DateTimeOffset(releaseDate.Value, TimeSpan.Zero);
            if (from != null && value < from) return false;
            if (to != null && value > to) return false;
            return true;
        }

        private static bool MatchesLastModified(DateTime? lastModified, DateTimeOffset? since)
        {
            if (since == null) return true;
            if (lastModified == null) return false;
            var value = new DateTimeOffset(lastModified.Value, TimeSpan.Zero);
            return value >= since.Value;
        }
    }
}
