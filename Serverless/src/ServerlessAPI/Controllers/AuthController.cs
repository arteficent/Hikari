using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ServerAPI.Abstraction;
using ServerAPI.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace ServerAPI.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly Abstraction.JwtConstants _jwtConstants;

        // For refresh token storage, using in-memory dictionary for sample. Replace with persistent store in production.
        private static readonly Dictionary<string, (string userId, DateTime expires)> RefreshTokens = new();

        public AuthController(IUserRepository userRepository, IOptions<Abstraction.JwtConstants> jwtConstants)
        {
            _userRepository = userRepository;
            _jwtConstants = jwtConstants?.Value?? new Abstraction.JwtConstants();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Email and password required");

            var user = await _userRepository.GetByEmailAsync(req.Email);
            if (user == null) return Unauthorized();

            if (!UserRepository.VerifyPassword(req.Password, user.PasswordHash))
                return Unauthorized();

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

        [HttpPost("refresh")]
        public IActionResult Refresh([FromBody] RefreshRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.RefreshToken)) return BadRequest();

            if (!RefreshTokens.TryGetValue(req.RefreshToken, out var entry)) return Unauthorized();
            if (entry.expires < DateTime.UtcNow) { RefreshTokens.Remove(req.RefreshToken); return Unauthorized(); }

            var userId = entry.userId;
            // load user
            var user = _userRepository.GetByIdAsync(userId).GetAwaiter().GetResult();
            if (user == null) return Unauthorized();

            var token = GenerateJwt(user);
            var newRefresh = GenerateRefreshToken(user.Id);
            // remove old
            RefreshTokens.Remove(req.RefreshToken);

            return Ok(new { token, refreshToken = newRefresh });
        }

        private string GenerateRefreshToken(string userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            RefreshTokens[token] = (userId, DateTime.UtcNow.AddDays(7));
            return token;
        }

        private string GenerateJwt(Entities.User user) 
        { 
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtConstants.Key));
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

            var token = new JwtSecurityToken(_jwtConstants.Issuer,
              _jwtConstants.Audience,
              claims,
              expires: DateTime.UtcNow.AddHours(_jwtConstants.DurationInHours),
              signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
