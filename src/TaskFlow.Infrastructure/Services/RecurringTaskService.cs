using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Recurring;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Services;

public class RecurringTaskService(IAppDbContext db) : IRecurringTaskService
{
    public async Task<IReadOnlyList<RecurringRuleDto>> ListAsync(CancellationToken ct = default)
    {
        var rules = await db.RecurringTaskRules.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
        return rules.Select(ToDto).ToList();
    }

    public async Task<RecurringRuleDto> CreateAsync(CreateRecurringRuleRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.TitleTemplate)) throw new ValidationAppException("error.validation");
        if (r.Interval < 1) throw new ValidationAppException("error.validation");
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct))
            throw new NotFoundAppException("error.not_found");

        var rule = new RecurringTaskRule
        {
            ProjectId = r.ProjectId,
            TaskListId = r.TaskListId,
            TitleTemplate = r.TitleTemplate.Trim(),
            Description = r.Description,
            Priority = r.Priority,
            AssigneeId = r.AssigneeId,
            Unit = r.Unit,
            Interval = r.Interval,
            DueInDays = r.DueInDays,
            IsActive = true,
            NextRunUtc = r.FirstRunUtc
        };
        db.RecurringTaskRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task ToggleAsync(long id, CancellationToken ct = default)
    {
        var rule = await db.RecurringTaskRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        rule.IsActive = !rule.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var rule = await db.RecurringTaskRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.RecurringTaskRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> GenerateDueAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        // Cross-tenant: bypass the tenant query filter; stamp TenantId explicitly.
        var dueRules = await db.RecurringTaskRules.IgnoreQueryFilters()
            .Where(r => r.IsActive && !r.IsDeleted && r.NextRunUtc <= nowUtc)
            .ToListAsync(ct);

        var generated = 0;
        foreach (var rule in dueRules)
        {
            // Catch up if several intervals elapsed, but cap to avoid runaway generation.
            var safety = 0;
            while (rule.NextRunUtc <= nowUtc && safety++ < 50)
            {
                db.Tasks.Add(new TaskItem
                {
                    TenantId = rule.TenantId,
                    ProjectId = rule.ProjectId,
                    TaskListId = rule.TaskListId,
                    Title = rule.TitleTemplate,
                    Description = rule.Description,
                    Priority = rule.Priority,
                    AssigneeId = rule.AssigneeId,
                    DueDate = rule.DueInDays is { } d ? rule.NextRunUtc.AddDays(d) : null,
                    Position = 1000
                });
                generated++;
                rule.LastRunUtc = rule.NextRunUtc;
                rule.NextRunUtc = rule.Advance(rule.NextRunUtc);
            }
        }

        if (generated > 0) await db.SaveChangesAsync(ct);
        return generated;
    }

    private static RecurringRuleDto ToDto(RecurringTaskRule r) => new(
        r.Id, r.ProjectId, r.TaskListId, r.TitleTemplate, r.Priority, r.AssigneeId,
        r.Unit, r.Interval, r.DueInDays, r.IsActive, r.NextRunUtc, r.LastRunUtc);
}
