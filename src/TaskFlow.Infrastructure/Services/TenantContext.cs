using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

// Scoped per request. Resolves tenant from the JWT "tenant_id" claim,
// with an override so seeding/registration can set it before a user exists.
public class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private long? _override;

    public long? TenantId
    {
        get
        {
            if (_override is not null) return _override;
            var claim = accessor.HttpContext?.User.FindFirstValue(JwtService.TenantIdClaim);
            return long.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool IsSuperAdmin =>
        accessor.HttpContext?.User.IsInRole("SuperAdmin") ?? false;

    public bool HasTenant => TenantId is not null;

    public void SetTenant(long tenantId) => _override = tenantId;
}
