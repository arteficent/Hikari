using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncServer.Identity.Models;
using SyncServer.Identity.Repositories;

namespace SyncServer.Identity.Controllers
{
    [ApiController]
    [Route("[controller]")]
    // Listing users, role assignment and deletion are reserved for the singleton
    // Root account. Admins do NOT have access to this controller.
    [Authorize(Roles = "Root")]
    public class AdminController : ControllerBase
    {
        private readonly IUserRepository users;

        public AdminController(IUserRepository users)
        {
            this.users = users;
        }

        [HttpGet("users")]
        public async Task<IActionResult> ListUsers()
        {
            var results = await users.ScanAllAsync();
            return Ok(results.Select(u => new { u.Id, u.Username, u.Roles, u.CreatedAt }));
        }

        [HttpPost("users/{id:guid}/roles")]
        public async Task<IActionResult> SetRoles([FromRoute] string id, [FromBody] List<Role> roles)
        {
            var user = await users.GetByIdAsync(id);
            if (user == null) return NotFound();

            // The Root role is not assignable via the API: it exists only on the
            // singleton bootstrap account.
            if (roles != null && roles.Contains(Role.Root))
                return BadRequest("The Root role cannot be assigned.");

            // A Root user cannot be demoted — Root is intended to be a single,
            // permanent account.
            if (user.Roles != null && user.Roles.Contains(Role.Root))
                return BadRequest("The Root user's roles cannot be changed.");

            user.Roles = roles;
            user.UpdatedAt = DateTime.UtcNow;
            var ok = await users.UpdateAsync(user);
            if (!ok) return StatusCode(500, "Failed to update roles");
            return NoContent();
        }

        [HttpDelete("users/{id:guid}")]
        public async Task<IActionResult> DeleteUser([FromRoute] string id)
        {
            var user = await users.GetByIdAsync(id);
            if (user == null) return NotFound();

            // The Root user is permanent.
            if (user.Roles != null && user.Roles.Contains(Role.Root))
                return BadRequest("The Root user cannot be deleted.");

            var ok = await users.DeleteAsync(id);
            if (!ok) return StatusCode(500, "Failed to delete user");
            return NoContent();
        }
    }
}
