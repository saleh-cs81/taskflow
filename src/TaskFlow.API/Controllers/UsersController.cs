using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.Users;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController(
    IUserAdminService users,
    TaskFlow.Application.Features.Projects.IProjectService projects,
    IValidator<UpdateUserRequest> updateValidator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Users.View)]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> List(CancellationToken ct)
        => Ok(await users.ListAsync(ct));

    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Users.View)]
    public async Task<ActionResult<UserListItemDto>> Get(long id, CancellationToken ct)
        => Ok(await users.GetAsync(id, ct));

    // Projects this user is a member of (for the "click a user → see their projects" view).
    [HttpGet("{id:long}/projects")]
    [RequirePermission(Permissions.Users.View)]
    public async Task<ActionResult<IReadOnlyList<TaskFlow.Application.Features.Projects.UserProjectDto>>> Projects(long id, CancellationToken ct)
        => Ok(await projects.ListForUserAsync(id, ct));

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<UserListItemDto>> Update(long id, UpdateUserRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await users.UpdateAsync(id, request, ct));
    }

    // Admin resets a user's password (no current-password required; signs that user out everywhere).
    [HttpPost("{id:long}/password")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> SetPassword(long id, SetPasswordRequest request, CancellationToken ct)
    {
        await users.SetPasswordAsync(id, request.NewPassword, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/roles")]
[Authorize]
public class RolesController(IUserAdminService users) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Users.View)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
        => Ok(await users.ListRolesAsync(ct));
}
