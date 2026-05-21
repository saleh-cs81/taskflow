using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Projects;

public record CreateProjectRequest(
    string Name,
    string? Code,
    string? Description,
    long? CategoryId,
    long? ClientId,
    DateTime? StartDate,
    DateTime? DueDate,
    bool IsBillable,
    decimal? BudgetAmount,
    decimal? BudgetHours,
    string? Color);

public record UpdateProjectRequest(
    string Name,
    string? Code,
    string? Description,
    long? CategoryId,
    long? ClientId,
    ProjectStatus Status,
    DateTime? StartDate,
    DateTime? DueDate,
    bool IsBillable,
    decimal? BudgetAmount,
    decimal? BudgetHours,
    string? Color);

public record ProjectDto(
    long Id,
    string Name,
    string? Code,
    string? Description,
    long? CategoryId,
    long? ClientId,
    ProjectStatus Status,
    DateTime? StartDate,
    DateTime? DueDate,
    bool IsBillable,
    decimal? BudgetAmount,
    decimal? BudgetHours,
    string? Color,
    int MemberCount,
    DateTime CreatedAtUtc);

public record AddProjectMemberRequest(long UserId, string? RoleInProject);
public record ProjectMemberDto(long Id, long UserId, string FullName, string Email, string? RoleInProject);

public record CreateMilestoneRequest(string Name, DateTime? DueDate);
public record UpdateMilestoneRequest(string Name, DateTime? DueDate, WorkStatus Status);
public record MilestoneDto(long Id, long ProjectId, string Name, DateTime? DueDate, WorkStatus Status);

// Kanban board payload.
public record BoardDto(long ProjectId, IReadOnlyList<BoardColumnDto> Columns);
public record BoardColumnDto(long Id, string Name, double Position, IReadOnlyList<BoardTaskDto> Tasks);
public record BoardTaskDto(long Id, string Title, WorkStatus Status, TaskPriority Priority, long? AssigneeId, DateTime? DueDate, double Position);
