using Microsoft.AspNetCore.Mvc;
using ServerlessAPI.Repositories;
using ServerlessAPI.Entities;
using System;
using System.Linq;

namespace Lambda.Controllers
{
    [ApiController]
    [Route("[controller]")]
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
            _logger.LogInformation($"Get request received: limit={limit}, genre={genre}, album={album}, playlist={playlist}, page={page}, pageSize={pageSize}, titlePrefix={titlePrefix}");

            Func<Music, bool> filter = m =>
                (string.IsNullOrEmpty(genre) || m.Genre == genre) &&
                (string.IsNullOrEmpty(album) || m.Album == album) &&
                (string.IsNullOrEmpty(playlist) || (m.Tags != null && m.Tags.Contains(playlist))) &&
                (string.IsNullOrEmpty(titlePrefix) || (m.Title != null && m.Title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase)));

            var songs = await _musicRepository.GetMusicAsync(limit ?? pageSize, filter);
            var paged = songs.Skip((page - 1) * pageSize).Take(pageSize);

            return Ok(paged);
        }
    }
}
