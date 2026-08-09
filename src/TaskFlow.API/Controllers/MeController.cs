using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Tasks;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController(ICurrentUser currentUser, ITenantContext tenant, ITaskService tasks) : ControllerBase
{
    // Open tasks assigned to me across all projects (powers Home > My Day / My Tasks).
    [HttpGet("tasks")]
    public async Task<ActionResult<IReadOnlyList<AssignedTaskDto>>> MyTasks([FromQuery] bool includeDone = false, CancellationToken ct = default)
        => Ok(await tasks.ListMineAsync(includeDone, ct));

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
