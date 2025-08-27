using Amazon;
using Amazon.S3.Model;
using Lambda.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServerlessAPI.Repositories;
using ServerlessAPI.Entities;


namespace Lambda.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EditController : ControllerBase
    {
        private readonly ILogger<EditController> _logger;
        private readonly IMusicRepository _musicRepository;

        public EditController(ILogger<EditController> logger, IMusicRepository musicRepository)
        {
            _logger = logger;
            _musicRepository = musicRepository;
        }

        [HttpPatch("edit")]
        public async Task<IActionResult> Edit([FromBody] Music music)
        {
            _logger.LogInformation($"Edit request received for song: {music?.Title} (ID: {music?.Id})");

            if (music == null || music.Id == Guid.Empty)
            {
                return BadRequest("Invalid music metadata.");
            }

            try
            {
                var updated = await _musicRepository.UpdateAsync(music);
                if (updated)
                {
                    _logger.LogInformation($"Metadata for song {music.Title} updated successfully.");
                    return Ok(new { Message = "Metadata update successful" });
                }
                else
                {
                    _logger.LogError($"Failed to update metadata for song {music.Title}.");
                    return StatusCode(500, "Failed to update metadata.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred while updating metadata for song {music.Title}");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}