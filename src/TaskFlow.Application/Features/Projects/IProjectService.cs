using TaskFlow.Application.Common.Models;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Projects;

public interface IProjectService
{
    Task<PagedResult<ProjectDto>> ListAsync(PageQuery page, ProjectStatus? status, string? search, CancellationToken ct = default);
    Task<ProjectDto> GetAsync(long id, CancellationToken ct = default);
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken ct = default);
    Task<ProjectDto> UpdateAsync(long id, UpdateProjectRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectMemberDto>> GetMembersAsync(long projectId, CancellationToken ct = default);
    Task<ProjectMemberDto> AddMemberAsync(long projectId, AddProjectMemberRequest request, CancellationToken ct = default);
    Task RemoveMemberAsync(long projectId, long userId, CancellationToken ct = default);

    Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(long projectId, CancellationToken ct = default);
    Task<MilestoneDto> CreateMilestoneAsync(long projectId, CreateMilestoneRequest request, CancellationToken ct = default);
    Task<MilestoneDto> UpdateMilestoneAsync(long projectId, long milestoneId, UpdateMilestoneRequest request, CancellationToken ct = default);
    Task DeleteMilestoneAsync(long projectId, long milestoneId, CancellationToken ct = default);

    Task<BoardDto> GetBoardAsync(long projectId, CancellationToken ct = default);
}
