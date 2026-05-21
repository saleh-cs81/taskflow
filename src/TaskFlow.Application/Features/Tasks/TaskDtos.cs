using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Tasks;

public record CreateTaskRequest(
    long ProjectId,
    long? TaskListId,
    long? ParentTaskId,
    long? MilestoneId,
    string Title,
    string? Description,
    TaskPriority Priority,
    long? AssigneeId,
    DateTime? StartDate,
    DateTime? DueDate,
    decimal? EstimateHours,
    bool IsBillable);

public record UpdateTaskRequest(
    string Title,
    string? Description,
    WorkStatus Status,
    TaskPriority Priority,
    long? AssigneeId,
    long? MilestoneId,
    DateTime? StartDate,
    DateTime? DueDate,
    decimal? EstimateHours,
    bool IsBillable);

// Drag & drop: move a task to a list and/or reposition.
public record MoveTaskRequest(long? TaskListId, double Position, WorkStatus? Status);

public record TaskDto(
    long Id,
    long ProjectId,
    long? TaskListId,
    long? ParentTaskId,
    long? MilestoneId,
    string Title,
    string? Description,
    WorkStatus Status,
    TaskPriority Priority,
    long? AssigneeId,
    long? ReporterId,
    DateTime? StartDate,
    DateTime? DueDate,
    decimal? EstimateHours,
    DateTime? CompletedAtUtc,
    double Position,
    bool IsBillable,
    IReadOnlyList<string> Tags,
    int ChecklistTotal,
    int ChecklistDone,
    int SubtaskCount,
    DateTime CreatedAtUtc);

public record CreateTaskListRequest(long ProjectId, string Name);
public record TaskListDto(long Id, long ProjectId, string Name, double Position);

public record ChecklistItemRequest(string Text);
public record ChecklistItemDto(long Id, long TaskId, string Text, bool IsDone, double Position);

public record AddDependencyRequest(long PredecessorTaskId, DependencyType Type);
public record DependencyDto(long Id, long PredecessorTaskId, long SuccessorTaskId, DependencyType Type);

public record AddCommentRequest(string Body, long? ParentCommentId, IReadOnlyList<long>? MentionedUserIds = null);
public record CommentDto(long Id, long AuthorId, string Body, long? ParentCommentId, DateTime CreatedAtUtc);

public record TagRequest(string Name, string? Color);
public record TagDto(long Id, string Name, string? Color);
