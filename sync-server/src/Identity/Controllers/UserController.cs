using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncServer.Identity.Dtos;
using SyncServer.Identity.Models;
using SyncServer.Identity.Repositories;
using SyncServer.Identity.Services;

namespace SyncServer.Identity.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserRepository _repo;
        private readonly ILogger<UserController> _logger;
        private readonly ICurrentUserService _currentUserService;

        public UserController(IUserRepository repo, ILogger<UserController> logger, ICurrentUserService currentUserService)
        {
            _repo = repo;
            _logger = logger;
            _currentUserService = currentUserService;
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(req?.Email)) return BadRequest("Email is required.");
            if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
                return BadRequest("Password must be at least 8 characters.");
            var existing = await _repo.GetByEmailAsync(req.Email);
            if (existing != null) return Conflict("Email already in use");

            var user = new User
            {
                Email = req.Email,
                Roles = req.Roles
            };

            try
            {
                var created = await _repo.CreateAsync(user, req.Password);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { created.Id, created.Email });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user");
                return StatusCode(500, "Failed to create user");
            }
        }

        [Authorize(Roles = "User,Admin")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateUserRequest req)
        {
            if (!IsSelfOrAdmin(id)) return Forbid();
            if (req?.Metadata == null) return BadRequest("Metadata is required.");

            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();
            user.Playlist = req.Metadata.Playlist;
            user.Roles = req.Metadata.Roles ?? user.Roles;
            user.UpdatedAt = DateTime.UtcNow;
            var ok = await _repo.UpdateAsync(user);
            if (!ok)
            {
                return StatusCode(500, "Failed to update user");
            }

            return NoContent();
        }

        [Authorize(Roles = "User,Admin")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] string id)
        {
            if (!IsSelfOrAdmin(id)) return Forbid();

            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();
            return Ok(new { user.Id, user.Email, user.Playlist, user.Roles, user.CreatedAt, user.UpdatedAt });
        }

        [Authorize(Roles = "User,Admin")]
        [HttpGet("by-email")]
        public async Task<IActionResult> GetByEmail([FromQuery] string email)
        {
            var user = await _repo.GetByEmailAsync(email);
            if (user == null) return NotFound();
            if (!IsSelfOrAdmin(user.Id)) return Forbid();
            return Ok(new { user.Id, user.Email, user.Playlist, user.Roles, user.CreatedAt, user.UpdatedAt });
        }

        [Authorize(Roles = "User,Admin")]
        [HttpPost("{id:guid}/change-password")]
        public async Task<IActionResult> ChangePassword([FromRoute] string id, [FromBody] ChangePasswordRequest req)
        {
            if (!IsSelfOrAdmin(id)) return Forbid();
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");
            var ok = await _repo.ChangePasswordAsync(id, req.NewPassword);
            if (!ok) return NotFound();
            return NoContent();
        }

        [Authorize(Roles = "User,Admin")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete([FromRoute] string id)
        {
            if (!IsSelfOrAdmin(id)) return Forbid();
            var ok = await _repo.DeleteAsync(id);
            if (!ok) return NotFound();
            return NoContent();
        }

        private bool IsSelfOrAdmin(string id)
        {
            var cur = _currentUserService.CurrentUser;
            if (cur == null) return false;
            if (cur.Id == id) return true;
            return cur.Roles != null && cur.Roles.Contains(Role.Admin);
        }
    }
}
