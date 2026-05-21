using Microsoft.Extensions.Options;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Entities;
using TaskFlow.Infrastructure.Services;

namespace TaskFlow.UnitTests;

public class JwtServiceTests
{
    private sealed class FixedClock : IDateTime
    {
        public DateTime UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static JwtService CreateService()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "TaskFlow",
            Audience = "TaskFlow.Client",
            SigningKey = "unit-test-signing-key-which-is-long-enough-1234",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14
        });
        return new JwtService(options, new FixedClock());
    }

    [Fact]
    public void CreateTokens_issues_jwt_and_future_expiries()
    {
        var svc = CreateService();
        var user = new User { Id = 1, TenantId = 7, Email = "a@b.com", FullName = "A B", Locale = "en" };

        var pair = svc.CreateTokens(user, ["CompanyAdmin"], ["projects.view"]);

        Assert.Equal(3, pair.AccessToken.Split('.').Length); // header.payload.signature
        Assert.True(pair.AccessTokenExpiresUtc > new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(pair.RefreshTokenExpiresUtc > pair.AccessTokenExpiresUtc);
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
    }

    [Fact]
    public void HashRefreshToken_is_deterministic_and_non_reversible()
    {
        var svc = CreateService();
        var hash1 = svc.HashRefreshToken("raw-token");
        var hash2 = svc.HashRefreshToken("raw-token");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual("raw-token", hash1);
    }
}
