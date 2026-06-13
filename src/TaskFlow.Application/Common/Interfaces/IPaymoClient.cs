namespace TaskFlow.Application.Common.Interfaces;

// Shapes mirror the Paymo REST API (fields we migrate).
public record PaymoUser(long Id, string Name, string? Email, bool Active, string? Type);
public record PaymoClient(long Id, string Name, string? Email, string? Phone, string? Address, string? City, string? Country, string? Website, bool Active);
public record PaymoClientContact(long Id, long ClientId, string Name, string? Email, string? Phone, string? Position, bool IsMain);
public record PaymoProjectStatus(long Id, string Name);
public record PaymoProject(
    long Id, string Name, string? Description, bool Active,
    long? ClientId, long? StatusId, string? Code, string? Color, decimal? BudgetHours, bool Billable);
public record PaymoTaskList(long Id, long ProjectId, string Name, int Seq, long? MilestoneId);
public record PaymoMilestone(long Id, long ProjectId, string Name, DateTime? DueDate, bool Complete);
// AssigneeUserIds = all ids from Paymo task 'users'; first is treated as primary. Priority = Paymo 100/75/50/25.
// ThreadId links a task to its comment thread. StatusId = Paymo workflow status (mapped to WorkStatus).
public record PaymoTask(
    long Id, long ProjectId, long? TaskListId, string Name, string? Description, bool Complete,
    DateTime? DueDate, DateTime? StartDate, DateTime? CompletedOn, int Priority, int Seq, string? Code,
    long? ThreadId, IReadOnlyList<long> AssigneeUserIds, long? StatusId);
public record PaymoSubtask(long Id, long TaskId, string Name, bool Complete, int Seq);
public record PaymoDiscussion(long Id, long ProjectId, string Name, string? Description, long? ThreadId, long? UserId);
public record PaymoComment(long Id, long? ThreadId, string Content, long? UserId, DateTime? CreatedOn);
public record PaymoFile(long Id, string FileName, long? ProjectId, long? TaskId, long? DiscussionId, long? CommentId, long Size, string? Mime);
public record PaymoExpense(long Id, long? ClientId, long? ProjectId, decimal Amount, string? Currency, DateTime? Date, string? Notes);
public record PaymoInvoice(long Id, string Number, long? ClientId, string? Status, string? Currency, DateTime? Date, DateTime? DueDate, decimal Subtotal, decimal TaxAmount, decimal Total);
public record PaymoInvoicePayment(long Id, long InvoiceId, decimal Amount, DateTime? Date, string? Notes);
public record PaymoEstimate(long Id, string Number, long? ClientId, string? Status, string? Currency, DateTime? Date, DateTime? ExpiryDate, decimal Subtotal, decimal TaxAmount, decimal Total);
// Workflow status (per-project board column). Action is Paymo's "backlog"/"complete" marker; null for intermediate columns.
public record PaymoWorkflowStatus(long Id, string Name, long? WorkflowId, string? Action, int Seq);
// users_tasks: the assignment row whose Id is referenced by a booking's user_task_id.
public record PaymoUserTask(long Id, long UserId, long TaskId);
// Scheduling allocation. ProjectId/UserId/TaskId resolved via user_task_id (+ the filtered project).
public record PaymoBooking(long Id, long UserTaskId, long? ProjectId, long? UserId, long? TaskId, DateTime? StartDate, DateTime? EndDate, int HoursPerDay, string? Description);
// UserId = Paymo 'user_id' (who logged the time); Billable = real 'billable' flag; Billed = invoiced.
public record PaymoTimeEntry(
    long Id, long ProjectId, long? TaskId, long? UserId, DateTime Start, DateTime End,
    int DurationSeconds, string? Description, bool Billable, bool Billed);

public interface IPaymoClient
{
    Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoUser>> GetUsersAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoClient>> GetClientsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoClientContact>> GetClientContactsAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoProjectStatus>> GetProjectStatusesAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoMilestone>> GetMilestonesAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoSubtask>> GetSubtasksAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoDiscussion>> GetDiscussionsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoComment>> GetCommentsAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoFile>> GetFilesAsync(string apiKey, CancellationToken ct = default);
    // Downloads a file's binary content. Returns null if unavailable.
    Task<(byte[] Bytes, string ContentType)?> GetFileBytesAsync(string apiKey, long paymoFileId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoExpense>> GetExpensesAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoInvoice>> GetInvoicesAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoInvoicePayment>> GetInvoicePaymentsAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoEstimate>> GetEstimatesAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoWorkflowStatus>> GetWorkflowStatusesAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoUserTask>> GetUserTasksAsync(string apiKey, long paymoUserId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymoBooking>> GetBookingsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default);
}
