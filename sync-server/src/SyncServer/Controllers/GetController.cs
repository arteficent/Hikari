using Microsoft.AspNetCore.Mvc;
using SyncServer.Repositories;
using SyncServer.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;

namespace SyncServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Roles = "User,Admin")]
    public class GetController : ControllerBase
    {
        private readonly ILogger<GetController> _logger;
        private readonly IMusicRepository _musicRepository;

        public GetController(ILogger<GetController> logger, IMusicRepository musicRepository)
        {
            _logger = logger;
            _musicRepository = musicRepository;
        }

        [HttpGet("songs")]
        public async Task<IActionResult> Get(
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
            _logger.LogInformation("Get request received: limit={Limit}, genre={Genre}, album={Album}, artist={Artist}, playlist={Playlist}, releaseFrom={ReleaseFrom}, releaseTo={ReleaseTo}, lastModifiedSince={LastModifiedSince}, page={Page}, pageSize={PageSize}, titlePrefix={TitlePrefix}", limit, genre, album, artist, playlist, releaseFrom, releaseTo, lastModifiedSince, page, pageSize, titlePrefix);

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

            return Ok(paged);
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
