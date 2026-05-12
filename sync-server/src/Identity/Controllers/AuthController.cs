using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SyncServer.Configuration;
using SyncServer.Identity.Dtos;
using SyncServer.Identity.Models;
using SyncServer.Identity.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SyncServer.Identity.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private const int RefreshTokenLifetimeDays = 7;

        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly JwtSettings _jwtSettings;
        private readonly BootstrapAdminSettings _bootstrapAdmin;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IOptions<JwtSettings> jwtSettings,
            IOptions<BootstrapAdminSettings> bootstrapAdmin,
            ILogger<AuthController> logger)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _jwtSettings = jwtSettings?.Value ?? new JwtSettings();
            _bootstrapAdmin = bootstrapAdmin?.Value ?? new BootstrapAdminSettings();
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Username and password required");

            // 1. DB always wins. If a user row exists for this username, we authenticate
            //    against the stored hash and never consult the bootstrap credentials.
            var user = await _userRepository.GetByUsernameAsync(req.Username);
            if (user != null)
            {
                if (!UserRepository.VerifyPassword(req.Password, user.PasswordHash))
                    return Unauthorized();

                // One-time upgrade: a previously-seeded bootstrap account that pre-dates
                // the Root role still carries only [Admin]. Promote it to Root so the
                // singleton super-admin invariant holds for existing deployments.
                if (IsBootstrapUsername(user.Username) &&
                    (user.Roles == null || !user.Roles.Contains(Role.Root)))
                {
                    user.Roles = new List<Role> { Role.Root };
                    user.UpdatedAt = DateTime.UtcNow;
                    await _userRepository.UpdateAsync(user);
                    _logger.LogWarning(
                        "Upgraded existing bootstrap account '{Username}' to Root role.",
                        user.Username);
                }
            }
            else
            {
                // 2. No DB row. Fall back to the bootstrap admin credentials, but only
                //    for the configured bootstrap username — and only on a constant-time
                //    password match. On success, persist a copy as the Root user so
                //    that subsequent logins go through path (1) above.
                if (!IsBootstrapMatch(req.Username, req.Password))
                    return Unauthorized();

                user = await SeedBootstrapAdminAsync(req.Username, req.Password);
                _logger.LogWarning(
                    "Bootstrap root '{Username}' authenticated via env credentials and seeded into DB. " +
                    "Change this password immediately via /User/{{id}}/change-password.",
                    req.Username);
            }

            var token = GenerateJwt(user);
            var refresh = await IssueRefreshTokenAsync(user.Id);

            // Return token plus basic profile (omit PasswordHash)
            var profile = new
            {
                user.Id,
                user.Username,
                Playlist = user.Playlist,
                Roles = user.Roles,
                user.CreatedAt,
                user.UpdatedAt
            };

            return Ok(new { token, refreshToken = refresh, profile });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.RefreshToken)) return BadRequest();

            // Lookup hashes the supplied token before querying DynamoDB.
            // Returns null for missing OR expired tokens.
            var record = await _refreshTokenRepository.GetAsync(req.RefreshToken);
            if (record == null) return Unauthorized();

            var user = await _userRepository.GetByIdAsync(record.UserId);
            if (user == null)
            {
                // Stale token referencing a deleted user — clean it up.
                await _refreshTokenRepository.DeleteAsync(req.RefreshToken);
                return Unauthorized();
            }

            // Rotate: invalidate the old refresh token and mint a fresh pair.
            await _refreshTokenRepository.DeleteAsync(req.RefreshToken);

            var token = GenerateJwt(user);
            var newRefresh = await IssueRefreshTokenAsync(user.Id);

            return Ok(new { token, refreshToken = newRefresh });
        }

        private bool IsBootstrapMatch(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(_bootstrapAdmin.Username) ||
                string.IsNullOrWhiteSpace(_bootstrapAdmin.Password))
            {
                return false;
            }

            if (!string.Equals(username, _bootstrapAdmin.Username, StringComparison.OrdinalIgnoreCase))
                return false;

            // Constant-time compare to avoid leaking the bootstrap password length / prefix
            // through timing.
            var a = Encoding.UTF8.GetBytes(password);
            var b = Encoding.UTF8.GetBytes(_bootstrapAdmin.Password);
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private bool IsBootstrapUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(_bootstrapAdmin.Username)) return false;
            return string.Equals(username, _bootstrapAdmin.Username, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<User> SeedBootstrapAdminAsync(string username, string password)
        {
            var user = new User
            {
                Username = username,
                Roles = new List<Role> { Role.Root }
            };
            return await _userRepository.CreateAsync(user, password);
        }

        private async Task<string> IssueRefreshTokenAsync(string userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var expires = DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays);
            await _refreshTokenRepository.StoreAsync(token, userId, expires);
            return token;
        }

        private string GenerateJwt(User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim> {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                // Custom "username" claim — username is no longer constrained to email format.
                new Claim("username", user.Username)
            };

            if (user.Roles != null)
            {
                foreach (var r in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, r.ToString()));
                }
            }

            var token = new JwtSecurityToken(_jwtSettings.Issuer,
              _jwtSettings.Audience,
              claims,
              expires: DateTime.UtcNow.AddHours(_jwtSettings.DurationInHours),
              signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
