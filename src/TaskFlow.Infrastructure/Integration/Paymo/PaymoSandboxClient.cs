using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Integration.Paymo;

// Deterministic in-memory Paymo data source for local demos and tests
// (no external network). Selected when config Paymo:UseSandbox = true.
public class PaymoSandboxClient : IPaymoClient
{
    private static readonly PaymoUser[] Users =
    [
        new(5001, "Lina Haddad", "lina@demo.test", true, "Admin"),
        new(5002, "Omar Khalil", "omar@demo.test", true, "Employee"),
    ];

    private static readonly PaymoClient[] Clients =
    [
        new(6001, "Globex", "ceo@globex.test", "111", "1 Globex Rd", "Amman", "Jordan", "globex.test", true),
        new(6002, "Initech", "info@initech.test", "222", "2 Initech Ave", "Dubai", "UAE", "initech.test", true),
    ];

    private static readonly PaymoClientContact[] Contacts =
    [
        new(7001, 6001, "Hank Scorpio", "hank@globex.test", "111", "CEO", true),
        new(7002, 6002, "Bill Lumbergh", "bill@initech.test", "222", "Manager", true),
    ];

    private static readonly PaymoProjectStatus[] Statuses =
    [
        new(9001, "Active"), new(9002, "On Hold"), new(9003, "Completed"),
    ];

    private static readonly PaymoProject[] Projects =
    [
        new(1001, "Marketing Site", "Company marketing website", true, 6001, 9001, "MKT", "#fb7029", 120m, true),
        new(1002, "Mobile App", "iOS + Android app", true, 6002, 9002, "APP", "#3b82f6", 200m, false),
    ];

    private static readonly PaymoTaskList[] Lists =
    [
        new(2001, 1001, "Backlog", 1, 8001),
        new(2002, 1001, "In Progress", 2, null),
        new(2003, 1002, "Backlog", 1, null),
    ];

    private static readonly PaymoMilestone[] Milestones =
    [
        new(8001, 1001, "Launch v1", DateTime.UtcNow.AddDays(14), false),
    ];

    private static readonly PaymoTask[] Tasks =
    [
        new(3001, 1001, 2001, "Design landing page", "Hero + features", false, DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(-1), null, 75, 1, "MKT-1", 30001, [5001]),
        new(3002, 1001, 2002, "Set up analytics", null, false, null, null, null, 50, 2, "MKT-2", 30002, [5002, 5001]),
        new(3003, 1001, 2002, "Write copy", "Marketing copy", true, null, null, DateTime.UtcNow.AddDays(-2), 25, 3, "MKT-3", 30003, [5001]),
        new(3004, 1002, 2003, "Auth screens", "Login + signup", false, null, null, null, 100, 1, "APP-1", 30004, [5002]),
        new(3005, 1002, 2003, "Push notifications", null, false, null, null, null, 50, 2, "APP-2", null, []),
    ];

    private static readonly PaymoDiscussion[] Discussions =
    [
        new(9201, 1001, "Kickoff", "Welcome to the project!", 40001, 5001),
    ];

    private static readonly PaymoComment[] Comments =
    [
        new(9301, 30001, "Looks great, ship it.", 5002, DateTime.UtcNow.AddDays(-1)),   // on task 3001
        new(9302, 40001, "Thanks team!", 5001, DateTime.UtcNow.AddHours(-5)),            // on discussion 9201
    ];

    private static readonly PaymoFile[] Files =
    [
        new(9401, "brief.pdf", 1001, 3001, null, null, 1234, "application/pdf"),
    ];

    private static readonly PaymoSubtask[] Subtasks =
    [
        new(9101, 3001, "Draft wireframe", true, 1),
        new(9102, 3001, "Pick palette", false, 2),
        new(9103, 3004, "OAuth flow", false, 1),
    ];

    private static readonly PaymoTimeEntry[] TimeEntries =
    [
        new(4001, 1001, 3001, 5001, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2).AddHours(3), 3 * 3600, "Design work", true, false),
        new(4002, 1001, 3003, 5001, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1).AddHours(2), 2 * 3600, "Copywriting", true, true),
        new(4003, 1002, 3004, 5002, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1).AddHours(4), 4 * 3600, "Auth flow", false, false),
    ];

    private static readonly PaymoExpense[] Expenses =
    [
        new(4501, 6001, 1001, 250.00m, "USD", DateTime.UtcNow.AddDays(-10), "Stock photography"),
        new(4502, 6002, 1002, 99.00m, "USD", DateTime.UtcNow.AddDays(-5), "App Store fee"),
    ];

    private static readonly PaymoInvoice[] Invoices =
    [
        new(4601, "INV-1001", 6001, "sent", "USD", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(7), 1000.00m, 160.00m, 1160.00m),
        new(4602, "INV-1002", 6002, "paid", "USD", DateTime.UtcNow.AddDays(-20), DateTime.UtcNow.AddDays(-6), 2000.00m, 0m, 2000.00m),
    ];

    private static readonly PaymoInvoicePayment[] InvoicePayments =
    [
        new(4701, 4602, 2000.00m, DateTime.UtcNow.AddDays(-6), "Bank transfer"),
        new(4702, 4601, 500.00m, DateTime.UtcNow.AddDays(-2), "Partial payment"),
    ];

    private static readonly PaymoEstimate[] Estimates =
    [
        new(4801, "EST-1001", 6001, "draft", "USD", DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(27), 3000.00m, 480.00m, 3480.00m),
    ];

    public Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(apiKey));

    public Task<IReadOnlyList<PaymoUser>> GetUsersAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoUser>>(Users);

    public Task<IReadOnlyList<PaymoClient>> GetClientsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoClient>>(Clients);

    public Task<IReadOnlyList<PaymoClientContact>> GetClientContactsAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoClientContact>>(Contacts);

    public Task<IReadOnlyList<PaymoProjectStatus>> GetProjectStatusesAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoProjectStatus>>(Statuses);

    public Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoProject>>(Projects);

    public Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoTaskList>>(Lists.Where(l => l.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoMilestone>> GetMilestonesAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoMilestone>>(Milestones.Where(m => m.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoTask>>(Tasks.Where(t => t.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoSubtask>> GetSubtasksAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
    {
        var taskIds = Tasks.Where(t => t.ProjectId == paymoProjectId).Select(t => t.Id).ToHashSet();
        return Task.FromResult<IReadOnlyList<PaymoSubtask>>(Subtasks.Where(s => taskIds.Contains(s.TaskId)).ToList());
    }

    public Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoTimeEntry>>(TimeEntries.Where(t => t.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoDiscussion>> GetDiscussionsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoDiscussion>>(Discussions.Where(d => d.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoComment>> GetCommentsAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoComment>>(Comments);

    public Task<IReadOnlyList<PaymoFile>> GetFilesAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoFile>>(Files);

    public Task<(byte[] Bytes, string ContentType)?> GetFileBytesAsync(string apiKey, long paymoFileId, CancellationToken ct = default)
        => Task.FromResult<(byte[], string)?>((System.Text.Encoding.UTF8.GetBytes($"sandbox file {paymoFileId}"), "application/octet-stream"));

    public Task<IReadOnlyList<PaymoExpense>> GetExpensesAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoExpense>>(Expenses);

    public Task<IReadOnlyList<PaymoInvoice>> GetInvoicesAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoInvoice>>(Invoices);

    public Task<IReadOnlyList<PaymoInvoicePayment>> GetInvoicePaymentsAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoInvoicePayment>>(InvoicePayments);

    public Task<IReadOnlyList<PaymoEstimate>> GetEstimatesAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoEstimate>>(Estimates);
}
