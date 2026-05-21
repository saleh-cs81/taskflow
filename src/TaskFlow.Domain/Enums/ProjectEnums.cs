namespace TaskFlow.Domain.Enums;

public enum ProjectStatus
{
    Planned = 0,
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Archived = 4,
    Cancelled = 5
}

// Status of a task or milestone.
public enum WorkStatus
{
    Todo = 0,
    InProgress = 1,
    InReview = 2,
    Done = 3,
    Blocked = 4
}

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

// Task dependency types (Gantt).
public enum DependencyType
{
    FinishToStart = 0,
    StartToStart = 1,
    FinishToFinish = 2,
    StartToFinish = 3
}

// Polymorphic owner for comments. Stored as int.
public enum CommentTargetType
{
    Task = 0,
    Project = 1,
    Milestone = 2
}
