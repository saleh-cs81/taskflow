using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Projects;
using TaskFlow.Domain.Enums;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/projects")]
[Authorize]
public class ProjectsController(
    IProjectService projects,
    TaskFlow.Application.Features.Files.IFileService files,
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator,
    IValidator<CreateMilestoneRequest> milestoneValidator) : ControllerBase
{
    // All files anywhere in the project (project-level + on its tasks + task comments).
    [HttpGet("{id:long}/files")]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<IReadOnlyList<TaskFlow.Application.Features.Files.FileDto>>> Files(long id, CancellationToken ct)
        => Ok(await files.ListForProjectAsync(id, ct));

    [HttpGet]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<PagedResult<ProjectDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] ProjectStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] long? departmentId = null,
        CancellationToken ct = default)
        => Ok(await projects.ListAsync(new PageQuery(page, pageSize), status, search, departmentId, ct));

    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<ProjectDto>> Get(long id, CancellationToken ct)
        => Ok(await projects.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.Projects.Create)]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAppAsync(request, ct);
        var dto = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    // Authorization is department-scoped inside the service (company admin, Projects.Update permission,
    // or admin of the project's department) — see ProjectService.UpdateAsync.
    [HttpPut("{id:long}")]
    public async Task<ActionResult<ProjectDto>> Update(long id, UpdateProjectRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await projects.UpdateAsync(id, request, ct));
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Projects.Delete)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await projects.DeleteAsync(id, ct);
        return NoContent();
    }

    // Hard-delete the project and ALL of its data (tasks, files, milestones, discussions, time, members).
    [HttpDelete("{id:long}/data")]
    [RequirePermission(Permissions.Projects.Delete)]
    public async Task<IActionResult> PurgeData(long id, CancellationToken ct)
    {
        await projects.PurgeAsync(id, ct);
        return NoContent();
    }

    // --- Members ---

    [HttpGet("{id:long}/members")]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> GetMembers(long id, CancellationToken ct)
        => Ok(await projects.GetMembersAsync(id, ct));

    // Department-scoped: a department admin may add any user to their department's projects (checked in the service).
    [HttpPost("{id:long}/members")]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(long id, AddProjectMemberRequest request, CancellationToken ct)
        => Ok(await projects.AddMemberAsync(id, request, ct));

    [HttpDelete("{id:long}/members/{userId:long}")]
    public async Task<IActionResult> RemoveMember(long id, long userId, CancellationToken ct)
    {
        await projects.RemoveMemberAsync(id, userId, ct);
        return NoContent();
    }

    // --- Finance ---
    [HttpGet("{id:long}/finance")]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<ProjectFinanceDto>> Finance(long id, CancellationToken ct) => Ok(await projects.GetFinanceAsync(id, ct));

    // --- Milestones ---

    [HttpGet("{id:long}/milestones")]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<IReadOnlyList<MilestoneDto>>> GetMilestones(long id, CancellationToken ct)
        => Ok(await projects.GetMilestonesAsync(id, ct));

    [HttpPost("{id:long}/milestones")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<ActionResult<MilestoneDto>> CreateMilestone(long id, CreateMilestoneRequest request, CancellationToken ct)
    {
        await milestoneValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await projects.CreateMilestoneAsync(id, request, ct));
    }

    [HttpPut("{id:long}/milestones/{milestoneId:long}")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<ActionResult<MilestoneDto>> UpdateMilestone(long id, long milestoneId, UpdateMilestoneRequest request, CancellationToken ct)
        => Ok(await projects.UpdateMilestoneAsync(id, milestoneId, request, ct));

    [HttpDelete("{id:long}/milestones/{milestoneId:long}")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<IActionResult> DeleteMilestone(long id, long milestoneId, CancellationToken ct)
    {
        await projects.DeleteMilestoneAsync(id, milestoneId, ct);
        return NoContent();
    }

    // --- Kanban board ---

    [HttpGet("{id:long}/board")]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<BoardDto>> GetBoard(long id, CancellationToken ct)
        => Ok(await projects.GetBoardAsync(id, ct));
}
