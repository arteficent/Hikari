using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SyncServer.Configuration;
using SyncServer.Identity.Dtos;
using SyncServer.Identity.Models;
using SyncServer.Identity.Repositories;
using System.Collections.Concurrent;
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
        private readonly IUserRepository _userRepository;
        private readonly JwtSettings _jwtSettings;
        private readonly BootstrapAdminSettings _bootstrapAdmin;
        private readonly ILogger<AuthController> _logger;

        // TODO: Move refresh tokens to a persistent store (DynamoDB/Redis) for multi-instance deployments.
        private static readonly ConcurrentDictionary<string, (string userId, DateTime expires)> RefreshTokens = new();

        public AuthController(
            IUserRepository userRepository,
            IOptions<JwtSettings> jwtSettings,
            IOptions<BootstrapAdminSettings> bootstrapAdmin,
            ILogger<AuthController> logger)
        {
            _userRepository = userRepository;
            _jwtSettings = jwtSettings?.Value ?? new JwtSettings();
            _bootstrapAdmin = bootstrapAdmin?.Value ?? new BootstrapAdminSettings();
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Email and password required");

            // 1. DB always wins. If a user row exists for this email, we authenticate
            //    against the stored hash and never consult the bootstrap credentials.
            var user = await _userRepository.GetByEmailAsync(req.Email);
            if (user != null)
            {
                if (!UserRepository.VerifyPassword(req.Password, user.PasswordHash))
                    return Unauthorized();
            }
            else
            {
                // 2. No DB row. Fall back to the bootstrap admin credentials, but only
                //    for the configured bootstrap email — and only on a constant-time
                //    password match. On success, persist a copy as an Admin user so
                //    that subsequent logins go through path (1) above.
                if (!IsBootstrapMatch(req.Email, req.Password))
                    return Unauthorized();

                user = await SeedBootstrapAdminAsync(req.Email, req.Password);
                _logger.LogWarning(
                    "Bootstrap admin '{Email}' authenticated via env credentials and seeded into DB. " +
                    "Change this password immediately via /User/{{id}}/change-password.",
                    req.Email);
            }

            var token = GenerateJwt(user);
            var refresh = GenerateRefreshToken(user.Id);

            // Return token plus basic profile (omit PasswordHash)
            var profile = new
            {
                user.Id,
                user.Email,
                Playlist = user.Playlist,
                Roles = user.Roles,
                user.CreatedAt,
                user.UpdatedAt
            };

            return Ok(new { token, refreshToken = refresh, profile });
        }

        private bool IsBootstrapMatch(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(_bootstrapAdmin.Email) ||
                string.IsNullOrWhiteSpace(_bootstrapAdmin.Password))
            {
                return false;
            }

            if (!string.Equals(email, _bootstrapAdmin.Email, StringComparison.OrdinalIgnoreCase))
                return false;

            // Constant-time compare to avoid leaking the bootstrap password length / prefix
            // through timing.
            var a = Encoding.UTF8.GetBytes(password);
            var b = Encoding.UTF8.GetBytes(_bootstrapAdmin.Password);
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private async Task<User> SeedBootstrapAdminAsync(string email, string password)
        {
            var user = new User
            {
                Email = email,
                Roles = new List<Role> { Role.Admin }
            };
            return await _userRepository.CreateAsync(user, password);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.RefreshToken)) return BadRequest();

            if (!RefreshTokens.TryGetValue(req.RefreshToken, out var entry)) return Unauthorized();
            if (entry.expires < DateTime.UtcNow) { RefreshTokens.TryRemove(req.RefreshToken, out _); return Unauthorized(); }

            var userId = entry.userId;
            // load user
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return Unauthorized();

            var token = GenerateJwt(user);
            var newRefresh = GenerateRefreshToken(user.Id);
            // remove old
            RefreshTokens.TryRemove(req.RefreshToken, out _);

            return Ok(new { token, refreshToken = newRefresh });
        }

        private string GenerateRefreshToken(string userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            RefreshTokens.TryAdd(token, (userId, DateTime.UtcNow.AddDays(7)));
            return token;
        }

        private string GenerateJwt(User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            // Add role claims if user has roles reserved in user object
            var claims = new List<Claim> {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email)
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
