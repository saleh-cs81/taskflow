using TaskFlow.Application.Common.Models;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Projects;

public interface IProjectService
{
    Task<PagedResult<ProjectDto>> ListAsync(PageQuery page, ProjectStatus? status, string? search, long? departmentId = null, CancellationToken ct = default);
    Task<ProjectDto> GetAsync(long id, CancellationToken ct = default);
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct = default);
    Task<ProjectDto> UpdateAsync(long id, UpdateProjectRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    // Hard-delete the project AND all of its data (tasks, files, milestones, discussions, time, members);
    // also resets its migration catalog entry so it can be re-imported cleanly.
    Task PurgeAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMemberDto>> GetMembersAsync(long projectId, CancellationToken ct = default);
    Task<IReadOnlyList<UserProjectDto>> ListForUserAsync(long userId, CancellationToken ct = default);
    Task<ProjectMemberDto> AddMemberAsync(long projectId, AddProjectMemberRequest request, CancellationToken ct = default);
    Task RemoveMemberAsync(long projectId, long userId, CancellationToken ct = default);

    Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(long projectId, CancellationToken ct = default);
    Task<MilestoneDto> CreateMilestoneAsync(long projectId, CreateMilestoneRequest request, CancellationToken ct = default);
    Task<MilestoneDto> UpdateMilestoneAsync(long projectId, long milestoneId, UpdateMilestoneRequest request, CancellationToken ct = default);
    Task DeleteMilestoneAsync(long projectId, long milestoneId, CancellationToken ct = default);

    Task<BoardDto> GetBoardAsync(long projectId, CancellationToken ct = default);
    Task<ProjectFinanceDto> GetFinanceAsync(long projectId, CancellationToken ct = default);
}
