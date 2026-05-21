using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.Org;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/clients")]
[Authorize]
public class ClientsController(IClientService svc) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.Projects.Create)]
    public async Task<ActionResult<ClientDto>> Create(ClientRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<ActionResult<ClientDto>> Update(long id, ClientRequest r, CancellationToken ct) => Ok(await svc.UpdateAsync(id, r, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Projects.Delete)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}

[ApiController]
[Route("api/v1/departments")]
[Authorize]
public class DepartmentsController(IDepartmentService svc) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Users.View)]
    public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<DepartmentDto>> Create(DepartmentRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<DepartmentDto>> Update(long id, DepartmentRequest r, CancellationToken ct) => Ok(await svc.UpdateAsync(id, r, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}

[ApiController]
[Route("api/v1/project-categories")]
[Authorize]
public class ProjectCategoriesController(ICategoryService svc) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.Projects.Create)]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<ActionResult<CategoryDto>> Update(long id, CategoryRequest r, CancellationToken ct) => Ok(await svc.UpdateAsync(id, r, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Projects.Delete)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}

[ApiController]
[Route("api/v1/discussions")]
[Authorize]
public class DiscussionsController(IDiscussionService svc) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<DiscussionDto>>> List([FromQuery] long projectId, CancellationToken ct)
        => Ok(await svc.ListAsync(projectId, ct));

    [HttpPost]
    [RequirePermission(Permissions.Tasks.Create)]
    public async Task<ActionResult<DiscussionDto>> Create(CreateDiscussionRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));

    [HttpGet("{id:long}/posts")]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<DiscussionPostDto>>> Posts(long id, CancellationToken ct) => Ok(await svc.GetPostsAsync(id, ct));

    [HttpPost("{id:long}/posts")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<DiscussionPostDto>> AddPost(long id, CreatePostRequest r, CancellationToken ct) => Ok(await svc.AddPostAsync(id, r, ct));
}
