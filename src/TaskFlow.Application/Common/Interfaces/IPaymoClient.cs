namespace TaskFlow.Application.Common.Interfaces;

// Shapes mirror the Paymo REST API (simplified to the fields we migrate).
public record PaymoUser(long Id, string Name, string? Email, bool Active);
public record PaymoProject(long Id, string Name, string? Description, bool Active);
public record PaymoTaskList(long Id, long ProjectId, string Name);
// AssigneeUserId = first id from Paymo's task 'users' array; Priority = Paymo 100/75/50/25.
public record PaymoTask(long Id, long ProjectId, long? TaskListId, string Name, string? Description, bool Complete, DateTime? DueDate, long? AssigneeUserId, int Priority);
// UserId = Paymo 'user_id' (who logged the time).
public record PaymoTimeEntry(long Id, long ProjectId, long? TaskId, long? UserId, DateTime Start, DateTime End, int DurationSeconds, string? Description, bool Billable);

public interface IPaymoClient
{
    Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoUser>> GetUsersAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
}
