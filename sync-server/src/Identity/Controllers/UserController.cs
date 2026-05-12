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

        // User creation is an administrative action.
        //   Root  → may create User or Admin accounts.
        //   Admin → may create User accounts only.
        // The Root role itself is never assignable: only the bootstrap account holds it.
        [Authorize(Roles = "Root,Admin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(req?.Username)) return BadRequest("Username is required.");
            if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
                return BadRequest("Password must be at least 8 characters.");
            var existing = await _repo.GetByUsernameAsync(req.Username);
            if (existing != null) return Conflict("Username already in use");

            var requestedRoles = req.Roles ?? new List<Role> { Role.User };
            if (requestedRoles.Contains(Role.Root))
                return BadRequest("The Root role cannot be assigned.");

            var caller = _currentUserService.CurrentUser;
            var callerIsRoot = caller?.Roles != null && caller.Roles.Contains(Role.Root);
            // Non-root callers (i.e. Admins) may only create plain users.
            if (!callerIsRoot && requestedRoles.Contains(Role.Admin))
                return Forbid();

            var user = new User
            {
                Username = req.Username,
                Roles = requestedRoles
            };

            try
            {
                var created = await _repo.CreateAsync(user, req.Password);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { created.Id, created.Username });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user");
                return StatusCode(500, "Failed to create user");
            }
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateUserRequest req)
        {
            if (!IsSelfOrPrivileged(id)) return Forbid();
            if (req?.Metadata == null) return BadRequest("Metadata is required.");

            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();
            user.Playlist = req.Metadata.Playlist;
            // Roles must be changed via the Admin/roles endpoint (Root-only). Ignore
            // any roles supplied through the generic update path.
            user.UpdatedAt = DateTime.UtcNow;
            var ok = await _repo.UpdateAsync(user);
            if (!ok)
            {
                return StatusCode(500, "Failed to update user");
            }

            return NoContent();
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] string id)
        {
            if (!IsSelfOrPrivileged(id)) return Forbid();

            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();
            return Ok(new { user.Id, user.Username, user.Playlist, user.Roles, user.CreatedAt, user.UpdatedAt });
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpGet("by-username")]
        public async Task<IActionResult> GetByUsername([FromQuery] string username)
        {
            var user = await _repo.GetByUsernameAsync(username);
            if (user == null) return NotFound();
            if (!IsSelfOrPrivileged(user.Id)) return Forbid();
            return Ok(new { user.Id, user.Username, user.Playlist, user.Roles, user.CreatedAt, user.UpdatedAt });
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpPost("{id:guid}/change-password")]
        public async Task<IActionResult> ChangePassword([FromRoute] string id, [FromBody] ChangePasswordRequest req)
        {
            if (!IsSelfOrPrivileged(id)) return Forbid();
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");
            var ok = await _repo.ChangePasswordAsync(id, req.NewPassword);
            if (!ok) return NotFound();
            return NoContent();
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpPost("{id:guid}/change-username")]
        public async Task<IActionResult> ChangeUsername([FromRoute] string id, [FromBody] UpdateUsernameRequest req)
        {
            if (!IsSelfOrPrivileged(id)) return Forbid();
            if (string.IsNullOrWhiteSpace(req?.Username)) return BadRequest("Username is required.");

            var user = await _repo.GetByIdAsync(id);
            if (user == null) return NotFound();

            // No-op if unchanged.
            if (string.Equals(user.Username, req.Username, StringComparison.OrdinalIgnoreCase))
                return NoContent();

            var existing = await _repo.GetByUsernameAsync(req.Username);
            if (existing != null && existing.Id != id) return Conflict("Username already in use");

            user.Username = req.Username;
            user.UpdatedAt = DateTime.UtcNow;
            var ok = await _repo.UpdateAsync(user);
            if (!ok) return StatusCode(500, "Failed to update username");
            return NoContent();
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpGet("me")]
        public async Task<IActionResult> GetCurrent()
        {
            var cur = _currentUserService.CurrentUser;
            if (cur == null) return Unauthorized();
            // Reload from repo so we return fresh roles/email even if the JWT is slightly stale.
            var user = await _repo.GetByIdAsync(cur.Id) ?? cur;
            return Ok(new { user.Id, user.Username, user.Roles, user.CreatedAt, user.UpdatedAt });
        }

        [Authorize(Roles = "User,Admin,Root")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete([FromRoute] string id)
        {
            if (!IsSelfOrPrivileged(id)) return Forbid();
            var ok = await _repo.DeleteAsync(id);
            if (!ok) return NotFound();
            return NoContent();
        }

        // Self, Admin, or Root may operate on the target id. Note: Admin/Root cannot
        // be granted access to a Root account's destructive operations (those route
        // through AdminController which is Root-only).
        private bool IsSelfOrPrivileged(string id)
        {
            var cur = _currentUserService.CurrentUser;
            if (cur == null) return false;
            if (cur.Id == id) return true;
            if (cur.Roles == null) return false;
            return cur.Roles.Contains(Role.Admin) || cur.Roles.Contains(Role.Root);
        }
    }
}
