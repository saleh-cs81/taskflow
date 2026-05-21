using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Recurring;

public record CreateRecurringRuleRequest(
    long ProjectId,
    long? TaskListId,
    string TitleTemplate,
    string? Description,
    TaskPriority Priority,
    long? AssigneeId,
    RecurrenceUnit Unit,
    int Interval,
    int? DueInDays,
    DateTime FirstRunUtc);

public record RecurringRuleDto(
    long Id,
    long ProjectId,
    long? TaskListId,
    string TitleTemplate,
    TaskPriority Priority,
    long? AssigneeId,
    RecurrenceUnit Unit,
    int Interval,
    int? DueInDays,
    bool IsActive,
    DateTime NextRunUtc,
    DateTime? LastRunUtc);

public interface IRecurringTaskService
{
    Task<IReadOnlyList<RecurringRuleDto>> ListAsync(CancellationToken ct = default);
    Task<RecurringRuleDto> CreateAsync(CreateRecurringRuleRequest request, CancellationToken ct = default);
    Task ToggleAsync(long id, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    // Generates task instances for all rules due at or before `nowUtc`, across all
    // tenants (used by the background worker and the manual run-now endpoint).
    Task<int> GenerateDueAsync(DateTime nowUtc, CancellationToken ct = default);
}
