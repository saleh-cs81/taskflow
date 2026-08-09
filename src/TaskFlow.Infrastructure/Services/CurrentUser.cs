using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public long? UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
                            ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(JwtService.PermissionClaim).Select(c => c.Value).ToArray() ?? [];

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];
}
