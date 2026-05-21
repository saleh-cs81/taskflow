using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.Tasks;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/task-lists")]
[Authorize]
public class TaskListsController(
    ITaskService tasks,
    IValidator<CreateTaskListRequest> validator) : ControllerBase
{
    [HttpPost]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<TaskListDto>> Create(CreateTaskListRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await tasks.CreateListAsync(request, ct));
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await tasks.DeleteListAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/tags")]
[Authorize]
public class TagsController(ITaskService tasks) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> List(CancellationToken ct)
        => Ok(await tasks.ListTagsAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<TagDto>> Create(TagRequest request, CancellationToken ct)
        => Ok(await tasks.CreateTagAsync(request, ct));
}
