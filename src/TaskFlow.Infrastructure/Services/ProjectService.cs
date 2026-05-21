using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Projects;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class ProjectService(IAppDbContext db) : IProjectService
{
    public async Task<PagedResult<ProjectDto>> ListAsync(PageQuery page, ProjectStatus? status, string? search, CancellationToken ct = default)
    {
        var query = db.Projects.AsNoTracking();
        if (status is not null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || (p.Code != null && p.Code.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip(page.Skip).Take(page.NormalizedSize)
            .Select(p => ToDto(p, p.Members.Count))
            .ToListAsync(ct);

        return new PagedResult<ProjectDto>(items, page.NormalizedPage, page.NormalizedSize, total);
    }

    public async Task<ProjectDto> GetAsync(long id, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => ToDto(p, p.Members.Count))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundAppException("error.not_found");
        return project;
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest r, CancellationToken ct = default)
    {
        var project = new Project
        {
            Name = r.Name.Trim(),
            Code = r.Code?.Trim(),
            Description = r.Description,
            CategoryId = r.CategoryId,
            ClientId = r.ClientId,
            Status = ProjectStatus.Planned,
            StartDate = r.StartDate,
            DueDate = r.DueDate,
            IsBillable = r.IsBillable,
            BudgetAmount = r.BudgetAmount,
            BudgetHours = r.BudgetHours,
            Color = r.Color
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        // Seed a default Kanban board.
        db.TaskLists.AddRange(
            new TaskList { ProjectId = project.Id, Name = "To Do", Position = 1000 },
            new TaskList { ProjectId = project.Id, Name = "In Progress", Position = 2000 },
            new TaskList { ProjectId = project.Id, Name = "Done", Position = 3000 });
        await db.SaveChangesAsync(ct);

        return ToDto(project, 0);
    }

    public async Task<ProjectDto> UpdateAsync(long id, UpdateProjectRequest r, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");

        project.Name = r.Name.Trim();
        project.Code = r.Code?.Trim();
        project.Description = r.Description;
        project.CategoryId = r.CategoryId;
        project.ClientId = r.ClientId;
        project.Status = r.Status;
        project.StartDate = r.StartDate;
        project.DueDate = r.DueDate;
        project.IsBillable = r.IsBillable;
        project.BudgetAmount = r.BudgetAmount;
        project.BudgetHours = r.BudgetHours;
        project.Color = r.Color;
        await db.SaveChangesAsync(ct);

        var memberCount = await db.ProjectMembers.CountAsync(m => m.ProjectId == id, ct);
        return ToDto(project, memberCount);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.Projects.Remove(project); // soft-deleted by interceptor
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> GetMembersAsync(long projectId, CancellationToken ct = default)
        => await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(m => new ProjectMemberDto(m.Id, m.UserId, m.User.FullName, m.User.Email, m.RoleInProject))
            .ToListAsync(ct);

    public async Task<ProjectMemberDto> AddMemberAsync(long projectId, AddProjectMemberRequest r, CancellationToken ct = default)
    {
        await EnsureProjectExists(projectId, ct);

        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == r.UserId, ct))
            throw new ConflictAppException("error.conflict");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == r.UserId, ct)
            ?? throw new NotFoundAppException("error.not_found");

        var member = new ProjectMember { ProjectId = projectId, UserId = r.UserId, RoleInProject = r.RoleInProject };
        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync(ct);

        return new ProjectMemberDto(member.Id, user.Id, user.FullName, user.Email, member.RoleInProject);
    }

    public async Task RemoveMemberAsync(long projectId, long userId, CancellationToken ct = default)
    {
        var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MilestoneDto>> GetMilestonesAsync(long projectId, CancellationToken ct = default)
        => await db.Milestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.DueDate)
            .Select(m => new MilestoneDto(m.Id, m.ProjectId, m.Name, m.DueDate, m.Status))
            .ToListAsync(ct);

    public async Task<MilestoneDto> CreateMilestoneAsync(long projectId, CreateMilestoneRequest r, CancellationToken ct = default)
    {
        await EnsureProjectExists(projectId, ct);
        var milestone = new Milestone { ProjectId = projectId, Name = r.Name.Trim(), DueDate = r.DueDate };
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync(ct);
        return new MilestoneDto(milestone.Id, projectId, milestone.Name, milestone.DueDate, milestone.Status);
    }

    public async Task<MilestoneDto> UpdateMilestoneAsync(long projectId, long milestoneId, UpdateMilestoneRequest r, CancellationToken ct = default)
    {
        var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        milestone.Name = r.Name.Trim();
        milestone.DueDate = r.DueDate;
        milestone.Status = r.Status;
        await db.SaveChangesAsync(ct);
        return new MilestoneDto(milestone.Id, projectId, milestone.Name, milestone.DueDate, milestone.Status);
    }

    public async Task DeleteMilestoneAsync(long projectId, long milestoneId, CancellationToken ct = default)
    {
        var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.Milestones.Remove(milestone);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BoardDto> GetBoardAsync(long projectId, CancellationToken ct = default)
    {
        await EnsureProjectExists(projectId, ct);

        var columns = await db.TaskLists.AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.Position)
            .Select(l => new BoardColumnDto(
                l.Id, l.Name, l.Position,
                l.Tasks.Where(t => t.ParentTaskId == null)
                    .OrderBy(t => t.Position)
                    .Select(t => new BoardTaskDto(t.Id, t.Title, t.Status, t.Priority, t.AssigneeId, t.DueDate, t.Position))
                    .ToList()))
            .ToListAsync(ct);

        return new BoardDto(projectId, columns);
    }

    private async Task EnsureProjectExists(long projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundAppException("error.not_found");
    }

    private static ProjectDto ToDto(Project p, int memberCount) => new(
        p.Id, p.Name, p.Code, p.Description, p.CategoryId, p.ClientId, p.Status,
        p.StartDate, p.DueDate, p.IsBillable, p.BudgetAmount, p.BudgetHours, p.Color,
        memberCount, p.CreatedAtUtc);
}
