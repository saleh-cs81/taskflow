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
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator,
    IValidator<CreateMilestoneRequest> milestoneValidator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<PagedResult<ProjectDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] ProjectStatus? status = null, [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await projects.ListAsync(new PageQuery(page, pageSize), status, search, ct));

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

    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Projects.Update)]
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

    // --- Members ---

    [HttpGet("{id:long}/members")]
    [RequirePermission(Permissions.Projects.View)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> GetMembers(long id, CancellationToken ct)
        => Ok(await projects.GetMembersAsync(id, ct));

    [HttpPost("{id:long}/members")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(long id, AddProjectMemberRequest request, CancellationToken ct)
        => Ok(await projects.AddMemberAsync(id, request, ct));

    [HttpDelete("{id:long}/members/{userId:long}")]
    [RequirePermission(Permissions.Projects.Update)]
    public async Task<IActionResult> RemoveMember(long id, long userId, CancellationToken ct)
    {
        await projects.RemoveMemberAsync(id, userId, ct);
        return NoContent();
    }

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
