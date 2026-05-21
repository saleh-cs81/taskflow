namespace TaskFlow.Application.Common.Interfaces;

// Shapes mirror the Paymo REST API (simplified to the fields we migrate).
public record PaymoProject(long Id, string Name, string? Description, bool Active);
public record PaymoTaskList(long Id, long ProjectId, string Name);
public record PaymoTask(long Id, long ProjectId, long? TaskListId, string Name, string? Description, bool Complete, DateTime? DueDate);
public record PaymoTimeEntry(long Id, long ProjectId, long? TaskId, DateTime Start, DateTime End, int DurationSeconds, string? Description, bool Billable);

public interface IPaymoClient
{
    Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
}
