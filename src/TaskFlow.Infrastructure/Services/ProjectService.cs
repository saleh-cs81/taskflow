using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Projects;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class ProjectService(IAppDbContext db, IDepartmentAccess access, ICurrentUser currentUser) : IProjectService
{
    public async Task<PagedResult<ProjectDto>> ListAsync(PageQuery page, ProjectStatus? status, string? search, long? departmentId, CancellationToken ct = default)
    {
        var query = db.Projects.AsNoTracking();
        if (status is not null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || (p.Code != null && p.Code.Contains(search)));
        if (departmentId is not null) query = query.Where(p => p.DepartmentId == departmentId);

        // Scoping: company admins see all. Everyone else sees only projects they're a member of,
        // plus projects in any department they administer (department admins).
        if (!access.IsCompanyAdmin)
        {
            var mine = await access.MyDepartmentIdsAsync(ct);
            var uid = currentUser.UserId;
            query = query.Where(p =>
                db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == uid)
                || (p.DepartmentId != null && mine.Contains(p.DepartmentId.Value)));
        }

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
            Color = r.Color,
            DepartmentId = await ResolveDeptAsync(r.Code?.Trim(), ct)
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        // Add the creator as a member so they see the project in their (now membership-scoped) list.
        if (currentUser.UserId is { } creatorId)
            db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = creatorId, RoleInProject = "Owner" });

        // Seed a default Kanban board.
        db.TaskLists.AddRange(
            new TaskList { ProjectId = project.Id, Name = "To Do", Position = 1000 },
            new TaskList { ProjectId = project.Id, Name = "In Progress", Position = 2000 },
            new TaskList { ProjectId = project.Id, Name = "Done", Position = 3000 });
        await db.SaveChangesAsync(ct);

        return ToDto(project, currentUser.UserId is null ? 0 : 1);
    }

    public async Task<ProjectDto> UpdateAsync(long id, UpdateProjectRequest r, CancellationToken ct = default)
    {
        if (!await access.CanManageProjectAsync(id, ct)) throw new ForbiddenAppException("error.forbidden");

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
        project.DepartmentId = await ResolveDeptAsync(project.Code, ct);
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

    public async Task PurgeAsync(long id, CancellationToken ct = default)
    {
        // Tenant-scoped check (the query filter enforces the project belongs to the caller's tenant).
        if (!await db.Projects.AnyAsync(p => p.Id == id, ct))
            throw new NotFoundAppException("error.not_found");

        // One atomic batch. Enum values: File TargetType Task=0/Project=1/Comment=2; Comment TargetType Task=0.
        // Uses set-based subqueries (not EF `List.Contains`, which fails to translate for large projects) so it
        // scales to any size; SET XACT_ABORT ON auto-rolls-back on any error (no partial deletes).
        const string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @p BIGINT = {0};
DECLARE @tasks TABLE(Id BIGINT PRIMARY KEY); INSERT @tasks SELECT Id FROM Tasks WHERE ProjectId=@p;
DECLARE @lists TABLE(Id BIGINT PRIMARY KEY); INSERT @lists SELECT Id FROM TaskLists WHERE ProjectId=@p;
DECLARE @miles TABLE(Id BIGINT PRIMARY KEY); INSERT @miles SELECT Id FROM Milestones WHERE ProjectId=@p;
DECLARE @books TABLE(Id BIGINT PRIMARY KEY); INSERT @books SELECT Id FROM Bookings WHERE ProjectId=@p;
DECLARE @entries TABLE(Id BIGINT PRIMARY KEY); INSERT @entries SELECT Id FROM TimeEntries WHERE ProjectId=@p;
DECLARE @discs TABLE(Id BIGINT PRIMARY KEY); INSERT @discs SELECT Id FROM Discussions WHERE ProjectId=@p;
DECLARE @cmts TABLE(Id BIGINT PRIMARY KEY); INSERT @cmts SELECT Id FROM Comments WHERE TargetType=0 AND TargetId IN (SELECT Id FROM @tasks);
DECLARE @files TABLE(Id BIGINT PRIMARY KEY); INSERT @files SELECT Id FROM Files
    WHERE (TargetType=1 AND TargetId=@p) OR (TargetType=0 AND TargetId IN (SELECT Id FROM @tasks)) OR (TargetType=2 AND TargetId IN (SELECT Id FROM @cmts));

DELETE FROM Bookings WHERE ProjectId=@p;
DELETE FROM Files WHERE Id IN (SELECT Id FROM @files);
DELETE FROM TimeEntries WHERE ProjectId=@p;
DELETE FROM ChecklistItems WHERE TaskId IN (SELECT Id FROM @tasks);
DELETE FROM TaskWatchers WHERE TaskId IN (SELECT Id FROM @tasks);
DELETE FROM TaskTags WHERE TaskId IN (SELECT Id FROM @tasks);
DELETE FROM TaskDependencies WHERE PredecessorTaskId IN (SELECT Id FROM @tasks) OR SuccessorTaskId IN (SELECT Id FROM @tasks);
DELETE FROM TaskAssignees WHERE TaskId IN (SELECT Id FROM @tasks);
DELETE FROM Comments WHERE Id IN (SELECT Id FROM @cmts);
DELETE FROM DiscussionPosts WHERE DiscussionId IN (SELECT Id FROM @discs);
DELETE FROM Discussions WHERE ProjectId=@p;
UPDATE Tasks SET ParentTaskId=NULL WHERE ProjectId=@p;
DELETE FROM Tasks WHERE ProjectId=@p;
DELETE FROM TaskLists WHERE ProjectId=@p;
DELETE FROM Milestones WHERE ProjectId=@p;
DELETE FROM ProjectMembers WHERE ProjectId=@p;
DELETE FROM Projects WHERE Id=@p;

DELETE FROM EntityMappings WHERE (EntityType='Project' AND LocalId=@p)
    OR (EntityType IN ('Task','TaskThread') AND LocalId IN (SELECT Id FROM @tasks))
    OR (EntityType='TaskList' AND LocalId IN (SELECT Id FROM @lists))
    OR (EntityType='Milestone' AND LocalId IN (SELECT Id FROM @miles))
    OR (EntityType='Booking' AND LocalId IN (SELECT Id FROM @books))
    OR (EntityType='TimeEntry' AND LocalId IN (SELECT Id FROM @entries))
    OR (EntityType='Comment' AND LocalId IN (SELECT Id FROM @cmts))
    OR (EntityType='File' AND LocalId IN (SELECT Id FROM @files))
    OR (EntityType='Discussion' AND LocalId IN (SELECT Id FROM @discs));

UPDATE MigrationProjectItems SET Status=0, LocalProjectId=NULL, RecordCount=0, ImportedAtUtc=NULL, ErrorMessage=NULL WHERE LocalProjectId=@p;
COMMIT;";
        await db.Database.ExecuteSqlRawAsync(sql, new object[] { id }, ct);
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> GetMembersAsync(long projectId, CancellationToken ct = default)
        => await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(m => new ProjectMemberDto(m.Id, m.UserId, m.User.FullName, m.User.Email, m.RoleInProject))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserProjectDto>> ListForUserAsync(long userId, CancellationToken ct = default)
        => await db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Project.Name)
            .Select(m => new UserProjectDto(m.ProjectId, m.Project.Name, m.Project.Status, m.RoleInProject))
            .ToListAsync(ct);

    public async Task<ProjectMemberDto> AddMemberAsync(long projectId, AddProjectMemberRequest r, CancellationToken ct = default)
    {
        await EnsureProjectExists(projectId, ct);
        if (!await access.CanManageProjectAsync(projectId, ct)) throw new ForbiddenAppException("error.forbidden");

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
        if (!await access.CanManageProjectAsync(projectId, ct)) throw new ForbiddenAppException("error.forbidden");
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

    public async Task<ProjectFinanceDto> GetFinanceAsync(long projectId, CancellationToken ct = default)
    {
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projectId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        var totalSecs = await db.TimeEntries.AsNoTracking().Where(t => t.ProjectId == projectId).SumAsync(t => (long?)t.DurationSeconds, ct) ?? 0;
        var billableSecs = await db.TimeEntries.AsNoTracking().Where(t => t.ProjectId == projectId && t.IsBillable).SumAsync(t => (long?)t.DurationSeconds, ct) ?? 0;
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.ProjectId == projectId).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        return new ProjectFinanceDto(p.BudgetAmount, p.BudgetHours,
            Math.Round(totalSecs / 3600.0, 1), Math.Round(billableSecs / 3600.0, 1), expenses, p.IsBillable);
    }

    private async Task EnsureProjectExists(long projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundAppException("error.not_found");
    }

    // Department for a project code: longest matching CodePrefix; none/no-code -> catch-all (empty prefix) dept.
    private async Task<long?> ResolveDeptAsync(string? code, CancellationToken ct)
    {
        var depts = await db.Departments.AsNoTracking().Select(d => new { d.Id, d.CodePrefix }).ToListAsync(ct);
        long? catchAll = depts.Where(d => string.IsNullOrEmpty(d.CodePrefix)).Select(d => (long?)d.Id).FirstOrDefault();
        if (string.IsNullOrEmpty(code)) return catchAll;
        var match = depts.Where(d => !string.IsNullOrEmpty(d.CodePrefix))
            .OrderByDescending(d => d.CodePrefix!.Length)
            .FirstOrDefault(d => code.StartsWith(d.CodePrefix!, StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? catchAll;
    }

    private static ProjectDto ToDto(Project p, int memberCount) => new(
        p.Id, p.Name, p.Code, p.Description, p.CategoryId, p.ClientId, p.Status,
        p.StartDate, p.DueDate, p.IsBillable, p.BudgetAmount, p.BudgetHours, p.Color,
        p.DepartmentId, memberCount, p.CreatedAtUtc);
}
