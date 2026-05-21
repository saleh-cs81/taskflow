using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

// A column / grouping within a project board (Kanban column).
public class TaskList : TenantEntity
{
    public long ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Position { get; set; }

    public Project Project { get; set; } = null!;
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}

// "Task" is avoided to prevent clashing with System.Threading.Tasks.Task.
public class TaskItem : TenantEntity
{
    public long ProjectId { get; set; }
    public long? TaskListId { get; set; }
    public long? ParentTaskId { get; set; }   // non-null => subtask
    public long? MilestoneId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public WorkStatus Status { get; set; } = WorkStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public long? AssigneeId { get; set; }
    public long? ReporterId { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? EstimateHours { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public double Position { get; set; }
    public bool IsBillable { get; set; }

    public Project Project { get; set; } = null!;
    public TaskList? TaskList { get; set; }
    public TaskItem? ParentTask { get; set; }
    public ICollection<TaskItem> SubTasks { get; set; } = new List<TaskItem>();
    public ICollection<ChecklistItem> ChecklistItems { get; set; } = new List<ChecklistItem>();
    public ICollection<TaskWatcher> Watchers { get; set; } = new List<TaskWatcher>();
    public ICollection<TaskTag> TaskTags { get; set; } = new List<TaskTag>();
}

public class TaskWatcher
{
    public long TaskId { get; set; }
    public long UserId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public User User { get; set; } = null!;
}

public class TaskDependency : TenantEntity
{
    public long PredecessorTaskId { get; set; }
    public long SuccessorTaskId { get; set; }
    public DependencyType Type { get; set; } = DependencyType.FinishToStart;

    public TaskItem PredecessorTask { get; set; } = null!;
    public TaskItem SuccessorTask { get; set; } = null!;
}

public class ChecklistItem : TenantEntity
{
    public long TaskId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public double Position { get; set; }

    public TaskItem Task { get; set; } = null!;
}

public class Tag : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }

    public ICollection<TaskTag> TaskTags { get; set; } = new List<TaskTag>();
}

public class TaskTag
{
    public long TaskId { get; set; }
    public long TagId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}

public class Comment : TenantEntity
{
    public CommentTargetType TargetType { get; set; }
    public long TargetId { get; set; }
    public long AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public long? ParentCommentId { get; set; }
}
