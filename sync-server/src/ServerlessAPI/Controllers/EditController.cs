using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerAPI.Repositories;
using ServerAPI.Entities;


namespace ServerAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Roles = "Admin")]
    public class EditController : ControllerBase
    {
        private readonly ILogger<EditController> _logger;
        private readonly IMusicRepository _musicRepository;

        public EditController(ILogger<EditController> logger, IMusicRepository musicRepository)
        {
            _logger = logger;
            _musicRepository = musicRepository;
        }

        [HttpPut]
        public async Task<IActionResult> Edit([FromBody] Music music)
        {
            _logger.LogInformation("Edit request received for song: {Title} (ID: {Id})", music?.Title, music?.Id);

            if (music == null || music.Id == Guid.Empty)
            {
                return BadRequest("Invalid music metadata.");
            }

            try
            {
                var updated = await _musicRepository.UpdateAsync(music);
                if (updated)
                {
                    _logger.LogInformation("Metadata for song {Title} updated successfully.", music.Title);
                    return Ok(new { Message = "Metadata update successful" });
                }
                else
                {
                    _logger.LogError("Failed to update metadata for song {Title}.", music.Title);
                    return StatusCode(500, "Failed to update metadata.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating metadata for song {Title}", music.Title);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}