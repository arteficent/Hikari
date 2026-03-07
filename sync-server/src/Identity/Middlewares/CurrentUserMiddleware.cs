using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using SyncServer.Identity.Models;
using SyncServer.Identity.Repositories;
using SyncServer.Identity.Services;

namespace SyncServer.Identity.Middlewares
{
    public class CurrentUserMiddleware
    {
        private readonly RequestDelegate _next;

        public CurrentUserMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IUserRepository users, ICurrentUserService currentUserService)
        {
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (!string.IsNullOrEmpty(sub))
                {
                    var user = await users.GetByIdAsync(sub);
                    if (user != null)
                    {
                        // attach the user object to HttpContext.Items for downstream handlers
                        context.Items["CurrentUser"] = user;
                        // also populate typed current user service
                        currentUserService.CurrentUser = user;
                    }
                }
            }

            await _next(context);
        }
    }

    public static class CurrentUserExtensions
    {
        public static User? GetCurrentUser(this HttpContext context)
        {
            if (context.Items.TryGetValue("CurrentUser", out var obj) && obj is User u) return u;
            return null;
        }

        public static string? GetCurrentUserId(this HttpContext context)
        {
            var u = context.GetCurrentUser();
            return u?.Id;
        }
    }
}
