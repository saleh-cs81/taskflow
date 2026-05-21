using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Integration.Paymo;

// Deterministic in-memory Paymo data source for local demos and tests
// (no external network). Selected when config Paymo:UseSandbox = true.
public class PaymoSandboxClient : IPaymoClient
{
    private static readonly PaymoProject[] Projects =
    [
        new(1001, "Marketing Site", "Company marketing website", true),
        new(1002, "Mobile App", "iOS + Android app", true),
    ];

    private static readonly PaymoTaskList[] Lists =
    [
        new(2001, 1001, "Backlog"),
        new(2002, 1001, "In Progress"),
        new(2003, 1002, "Backlog"),
    ];

    private static readonly PaymoTask[] Tasks =
    [
        new(3001, 1001, 2001, "Design landing page", "Hero + features", false, null),
        new(3002, 1001, 2002, "Set up analytics", null, false, null),
        new(3003, 1001, 2002, "Write copy", "Marketing copy", true, null),
        new(3004, 1002, 2003, "Auth screens", "Login + signup", false, null),
        new(3005, 1002, 2003, "Push notifications", null, false, null),
    ];

    private static readonly PaymoTimeEntry[] TimeEntries =
    [
        new(4001, 1001, 3001, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2).AddHours(3), 3 * 3600, "Design work", true),
        new(4002, 1001, 3003, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1).AddHours(2), 2 * 3600, "Copywriting", true),
        new(4003, 1002, 3004, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1).AddHours(4), 4 * 3600, "Auth flow", false),
    ];

    public Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(apiKey));

    public Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoProject>>(Projects);

    public Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoTaskList>>(Lists.Where(l => l.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoTask>>(Tasks.Where(t => t.ProjectId == paymoProjectId).ToList());

    public Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PaymoTimeEntry>>(TimeEntries.Where(t => t.ProjectId == paymoProjectId).ToList());
}
