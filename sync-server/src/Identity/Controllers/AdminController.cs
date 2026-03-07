using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyncServer.Identity.Models;
using SyncServer.Identity.Repositories;

namespace SyncServer.Identity.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Roles = "Admin")]
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
            return Ok(results.Select(u => new { u.Id, u.Email, u.Roles, u.CreatedAt }));
        }

        [HttpPost("users/{id:guid}/roles")]
        public async Task<IActionResult> SetRoles([FromRoute] string id, [FromBody] List<Role> roles)
        {
            var user = await users.GetByIdAsync(id);
            if (user == null) return NotFound();
            user.Roles = roles;
            var ok = await users.UpdateAsync(user);
            if (!ok) return StatusCode(500, "Failed to update roles");
            return NoContent();
        }
    }
}
