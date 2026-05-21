using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController(ICurrentUser currentUser, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        userId = currentUser.UserId,
        email = currentUser.Email,
        tenantId = tenant.TenantId,
        isSuperAdmin = tenant.IsSuperAdmin,
        permissions = currentUser.Permissions
    });

    // Demonstrates a permission-gated endpoint.
    [HttpGet("can-create-project")]
    [RequirePermission(Permissions.Projects.Create)]
    public IActionResult CanCreateProject() => Ok(new { allowed = true });
}
