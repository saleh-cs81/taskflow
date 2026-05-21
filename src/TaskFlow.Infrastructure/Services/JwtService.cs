using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Services;

public class JwtService(IOptions<JwtOptions> options, IDateTime clock) : IJwtService
{
    public const string TenantIdClaim = "tenant_id";
    public const string PermissionClaim = "permission";

    private readonly JwtOptions _opt = options.Value;

    public TokenPair CreateTokens(User user, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_opt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(TenantIdClaim, user.TenantId.ToString()),
            new("name", user.FullName),
            new("locale", user.Locale),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaim, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: accessExpires,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshExpires = now.AddDays(_opt.RefreshTokenDays);

        return new TokenPair(accessToken, accessExpires, rawRefresh, refreshExpires);
    }

    public string HashRefreshToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }
}
