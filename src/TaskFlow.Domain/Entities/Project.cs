using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

public class ProjectCategory : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

public class Project : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }

    public long? CategoryId { get; set; }
    public long? ClientId { get; set; }
    public long? DepartmentId { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }

    public bool IsBillable { get; set; }
    public decimal? BudgetAmount { get; set; }
    public decimal? BudgetHours { get; set; }
    public string? Color { get; set; }

    public ProjectCategory? Category { get; set; }
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
    public ICollection<TaskList> TaskLists { get; set; } = new List<TaskList>();
}

public class ProjectMember : TenantEntity
{
    public long ProjectId { get; set; }
    public long UserId { get; set; }
    public string? RoleInProject { get; set; }

    public Project Project { get; set; } = null!;
    public User User { get; set; } = null!;
}

public class Milestone : TenantEntity
{
    public long ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.Todo;

    public Project Project { get; set; } = null!;
}
