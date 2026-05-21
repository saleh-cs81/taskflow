using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Tasks;
using TaskFlow.Domain.Enums;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/tasks")]
[Authorize]
public class TasksController(
    ITaskService tasks,
    IValidator<CreateTaskRequest> createValidator,
    IValidator<UpdateTaskRequest> updateValidator,
    IValidator<AddCommentRequest> commentValidator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<PagedResult<TaskDto>>> List(
        [FromQuery] long projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] WorkStatus? status = null, [FromQuery] long? assigneeId = null, CancellationToken ct = default)
        => Ok(await tasks.ListAsync(projectId, new PageQuery(page, pageSize), status, assigneeId, ct));

    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<TaskDto>> Get(long id, CancellationToken ct)
        => Ok(await tasks.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.Tasks.Create)]
    public async Task<ActionResult<TaskDto>> Create(CreateTaskRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAppAsync(request, ct);
        var dto = await tasks.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<TaskDto>> Update(long id, UpdateTaskRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await tasks.UpdateAsync(id, request, ct));
    }

    // Drag & drop reposition / move between columns.
    [HttpPatch("{id:long}/move")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<TaskDto>> Move(long id, MoveTaskRequest request, CancellationToken ct)
        => Ok(await tasks.MoveAsync(id, request, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Tasks.Delete)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await tasks.DeleteAsync(id, ct);
        return NoContent();
    }

    // --- Checklist ---

    [HttpGet("{id:long}/checklist")]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<ChecklistItemDto>>> GetChecklist(long id, CancellationToken ct)
        => Ok(await tasks.GetChecklistAsync(id, ct));

    [HttpPost("{id:long}/checklist")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<ChecklistItemDto>> AddChecklistItem(long id, ChecklistItemRequest request, CancellationToken ct)
        => Ok(await tasks.AddChecklistItemAsync(id, request, ct));

    [HttpPatch("{id:long}/checklist/{itemId:long}/toggle")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<ChecklistItemDto>> ToggleChecklistItem(long id, long itemId, CancellationToken ct)
        => Ok(await tasks.ToggleChecklistItemAsync(id, itemId, ct));

    [HttpDelete("{id:long}/checklist/{itemId:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> DeleteChecklistItem(long id, long itemId, CancellationToken ct)
    {
        await tasks.DeleteChecklistItemAsync(id, itemId, ct);
        return NoContent();
    }

    // --- Watchers ---

    [HttpPost("{id:long}/watchers/{userId:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> AddWatcher(long id, long userId, CancellationToken ct)
    {
        await tasks.AddWatcherAsync(id, userId, ct);
        return NoContent();
    }

    [HttpDelete("{id:long}/watchers/{userId:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> RemoveWatcher(long id, long userId, CancellationToken ct)
    {
        await tasks.RemoveWatcherAsync(id, userId, ct);
        return NoContent();
    }

    // --- Dependencies ---

    [HttpPost("{id:long}/dependencies")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<DependencyDto>> AddDependency(long id, AddDependencyRequest request, CancellationToken ct)
        => Ok(await tasks.AddDependencyAsync(id, request, ct));

    [HttpDelete("{id:long}/dependencies/{dependencyId:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> RemoveDependency(long id, long dependencyId, CancellationToken ct)
    {
        await tasks.RemoveDependencyAsync(id, dependencyId, ct);
        return NoContent();
    }

    // --- Tags ---

    [HttpPost("{id:long}/tags/{tagId:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> AddTag(long id, long tagId, CancellationToken ct)
    {
        await tasks.AddTagToTaskAsync(id, tagId, ct);
        return NoContent();
    }

    [HttpDelete("{id:long}/tags/{tagId:long}")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> RemoveTag(long id, long tagId, CancellationToken ct)
    {
        await tasks.RemoveTagFromTaskAsync(id, tagId, ct);
        return NoContent();
    }

    // --- Comments ---

    [HttpGet("{id:long}/comments")]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> GetComments(long id, CancellationToken ct)
        => Ok(await tasks.GetCommentsAsync(id, ct));

    [HttpPost("{id:long}/comments")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<ActionResult<CommentDto>> AddComment(long id, AddCommentRequest request, CancellationToken ct)
    {
        await commentValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await tasks.AddCommentAsync(id, request, ct));
    }
}
