using TaskFlow.Domain.Entities;

namespace TaskFlow.Application.Common.Interfaces;

public record TokenPair(string AccessToken, DateTime AccessTokenExpiresUtc, string RefreshToken, DateTime RefreshTokenExpiresUtc);

public interface IJwtService
{
    // Issues an access token + a raw refresh token. Caller persists the hashed refresh token.
    TokenPair CreateTokens(User user, IEnumerable<string> roles, IEnumerable<string> permissions);

    string HashRefreshToken(string rawToken);
}
