using Microsoft.AspNetCore.Mvc;
using ServerAPI.Repositories;
using ServerAPI.Entities;
using Microsoft.AspNetCore.Authorization;

namespace ServerAPI.Controllers
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
            [FromQuery] string? playlist,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? titlePrefix = null)
        {
            _logger.LogInformation("Get request received: limit={Limit}, genre={Genre}, album={Album}, playlist={Playlist}, page={Page}, pageSize={PageSize}, titlePrefix={TitlePrefix}", limit, genre, album, playlist, page, pageSize, titlePrefix);

            Func<Music, bool> filter = m =>
                (string.IsNullOrEmpty(genre) || m.Genre == genre) &&
                (string.IsNullOrEmpty(album) || m.Album == album) &&
                (string.IsNullOrEmpty(playlist) || (m.Tags != null && m.Tags.Contains(playlist))) &&
                (string.IsNullOrEmpty(titlePrefix) || (m.Title != null && m.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase)));

            var songs = await _musicRepository.GetMusicAsync(limit ?? int.MaxValue, filter);
            var paged = songs.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(paged);
        }
    }
}
