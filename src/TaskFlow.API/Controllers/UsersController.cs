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

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<UserListItemDto>> Update(long id, UpdateUserRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await users.UpdateAsync(id, request, ct));
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
