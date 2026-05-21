using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Integration;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Integration.Paymo;

public class MigrationService(
    IAppDbContext db,
    IPaymoClient paymo,
    IKeyProtector protector,
    ICurrentUser currentUser,
    IDateTime clock,
    ILogger<MigrationService> logger) : IMigrationService
{
    public async Task<PaymoConnectionDto> GetStatusAsync(CancellationToken ct = default)
    {
        var conn = await db.PaymoConnections.AsNoTracking().FirstOrDefaultAsync(ct);
        return conn is null
            ? new PaymoConnectionDto(false, PaymoConnectionStatus.Disconnected, null)
            : new PaymoConnectionDto(conn.Status == PaymoConnectionStatus.Connected, conn.Status, conn.LastSyncUtc);
    }

    public async Task<PaymoConnectionDto> ConnectAsync(ConnectPaymoRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.ApiKey)) throw new ValidationAppException("error.validation");

        var valid = await paymo.ValidateKeyAsync(r.ApiKey, ct);
        if (!valid) throw new ValidationAppException("integration.invalid_key");

        var conn = await db.PaymoConnections.FirstOrDefaultAsync(ct);
        if (conn is null)
        {
            conn = new PaymoConnection();
            db.PaymoConnections.Add(conn);
        }
        conn.ApiKeyEncrypted = protector.Protect(r.ApiKey);
        conn.Status = PaymoConnectionStatus.Connected;
        await db.SaveChangesAsync(ct);

        return new PaymoConnectionDto(true, conn.Status, conn.LastSyncUtc);
    }

    public async Task<MigrationJobDto> RunAsync(MigrationJobType type, CancellationToken ct = default)
    {
        var conn = await db.PaymoConnections.FirstOrDefaultAsync(ct)
            ?? throw new ValidationAppException("integration.not_connected");
        var apiKey = protector.Unprotect(conn.ApiKeyEncrypted);
        var since = type == MigrationJobType.Incremental ? conn.LastSyncUtc : null;

        var job = new MigrationJob { Type = type, Status = MigrationJobStatus.Running, StartedUtc = clock.UtcNow };
        db.MigrationJobs.Add(job);
        await db.SaveChangesAsync(ct);

        try
        {
            var projects = await paymo.GetProjectsAsync(apiKey, since, ct);
            foreach (var p in projects)
            {
                long localProjectId;
                try
                {
                    localProjectId = await UpsertProjectAsync(p, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex)
                {
                    await RecordError(job, "Project", p.Id, ex, p, ct);
                    continue; // can't import children without the project
                }

                await ImportListsTasksTime(job, apiKey, p.Id, localProjectId, since, ct);
            }

            job.TotalRecords = job.ProcessedRecords + job.ErrorCount;
            job.Status = job.ErrorCount > 0 ? MigrationJobStatus.CompletedWithErrors : MigrationJobStatus.Completed;
            job.Message = $"Imported {job.ProcessedRecords} records ({job.ErrorCount} errors).";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Paymo migration job {JobId} failed", job.Id);
            job.Status = MigrationJobStatus.Failed;
            job.Message = ex.Message;
        }

        job.FinishedUtc = clock.UtcNow;
        conn.LastSyncUtc = job.FinishedUtc;
        await db.SaveChangesAsync(ct);

        return ToJobDto(job);
    }

    private async Task ImportListsTasksTime(MigrationJob job, string apiKey, long paymoProjectId, long localProjectId, DateTime? since, CancellationToken ct)
    {
        // Task lists
        var listMap = new Dictionary<long, long>();
        foreach (var l in await paymo.GetTaskListsAsync(apiKey, paymoProjectId, ct))
        {
            try { listMap[l.Id] = await UpsertTaskListAsync(l, localProjectId, ct); job.ProcessedRecords++; }
            catch (Exception ex) { await RecordError(job, "TaskList", l.Id, ex, l, ct); }
        }

        // Tasks
        var taskMap = new Dictionary<long, long>();
        foreach (var t in await paymo.GetTasksAsync(apiKey, paymoProjectId, since, ct))
        {
            try
            {
                long? localList = t.TaskListId is { } lid && listMap.TryGetValue(lid, out var ll) ? ll : null;
                taskMap[t.Id] = await UpsertTaskAsync(t, localProjectId, localList, ct);
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "Task", t.Id, ex, t, ct); }
        }

        // Time entries
        foreach (var e in await paymo.GetTimeEntriesAsync(apiKey, paymoProjectId, since, ct))
        {
            try
            {
                long? localTask = e.TaskId is { } tid && taskMap.TryGetValue(tid, out var lt) ? lt : null;
                await UpsertTimeEntryAsync(e, localProjectId, localTask, ct);
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "TimeEntry", e.Id, ex, e, ct); }
        }
    }

    // --- Idempotent upserts keyed by EntityMapping(EntityType, PaymoId) ---

    private async Task<long> UpsertProjectAsync(PaymoProject p, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) throw new InvalidOperationException("Project name is required.");
        var existing = await FindMappingAsync("Project", p.Id, ct);
        if (existing is not null)
        {
            var proj = await db.Projects.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (proj is not null) { proj.Name = p.Name; proj.Description = p.Description; proj.Status = p.Active ? ProjectStatus.Active : ProjectStatus.Archived; }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }

        var created = new Project { Name = p.Name, Description = p.Description, Status = p.Active ? ProjectStatus.Active : ProjectStatus.Archived };
        db.Projects.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Project", p.Id, created.Id, ct);
        return created.Id;
    }

    private async Task<long> UpsertTaskListAsync(PaymoTaskList l, long localProjectId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("TaskList", l.Id, ct);
        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return existing.LocalId; }

        var pos = (await db.TaskLists.Where(x => x.ProjectId == localProjectId).MaxAsync(x => (double?)x.Position, ct) ?? 0) + 1000;
        var created = new TaskList { ProjectId = localProjectId, Name = l.Name, Position = pos };
        db.TaskLists.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("TaskList", l.Id, created.Id, ct);
        return created.Id;
    }

    private async Task<long> UpsertTaskAsync(PaymoTask t, long localProjectId, long? localListId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) throw new InvalidOperationException("Task title is required.");
        var status = t.Complete ? WorkStatus.Done : WorkStatus.Todo;
        var existing = await FindMappingAsync("Task", t.Id, ct);
        if (existing is not null)
        {
            var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (task is not null)
            {
                task.Title = t.Name; task.Description = t.Description; task.TaskListId = localListId;
                task.Status = status; task.DueDate = t.DueDate;
                task.CompletedAtUtc = t.Complete ? (task.CompletedAtUtc ?? clock.UtcNow) : null;
            }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }

        var pos = (await db.Tasks.Where(x => x.TaskListId == localListId).MaxAsync(x => (double?)x.Position, ct) ?? 0) + 1000;
        var created = new TaskItem
        {
            ProjectId = localProjectId, TaskListId = localListId, Title = t.Name, Description = t.Description,
            Status = status, Priority = TaskPriority.Normal, DueDate = t.DueDate, Position = pos,
            CompletedAtUtc = t.Complete ? clock.UtcNow : null
        };
        db.Tasks.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Task", t.Id, created.Id, ct);
        return created.Id;
    }

    private async Task UpsertTimeEntryAsync(PaymoTimeEntry e, long localProjectId, long? localTaskId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("TimeEntry", e.Id, ct);
        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return; }

        var created = new TimeEntry
        {
            UserId = currentUser.UserId ?? throw new InvalidOperationException("No current user."),
            ProjectId = localProjectId, TaskId = localTaskId,
            StartUtc = e.Start, EndUtc = e.End, DurationSeconds = e.DurationSeconds,
            IsRunning = false, IsBillable = e.Billable, Note = e.Description, Source = TimeEntrySource.Manual
        };
        db.TimeEntries.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("TimeEntry", e.Id, created.Id, ct);
    }

    private Task<EntityMapping?> FindMappingAsync(string entityType, long paymoId, CancellationToken ct)
        => db.EntityMappings.FirstOrDefaultAsync(m => m.EntityType == entityType && m.PaymoId == paymoId, ct);

    private async Task AddMappingAsync(string entityType, long paymoId, long localId, CancellationToken ct)
    {
        db.EntityMappings.Add(new EntityMapping { EntityType = entityType, PaymoId = paymoId, LocalId = localId, LastSyncedUtc = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    private async Task RecordError(MigrationJob job, string entityType, long paymoId, Exception ex, object payload, CancellationToken ct)
    {
        logger.LogWarning(ex, "Migration error on {EntityType} {PaymoId}", entityType, paymoId);
        db.MigrationErrors.Add(new MigrationError
        {
            MigrationJobId = job.Id, EntityType = entityType, PaymoId = paymoId,
            Message = ex.Message, PayloadJson = JsonSerializer.Serialize(payload), RetryCount = 0
        });
        job.ErrorCount++;
        await db.SaveChangesAsync(ct);
    }

    public async Task<MigrationJobDto> GetJobAsync(long jobId, CancellationToken ct = default)
    {
        var job = await db.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        return ToJobDto(job);
    }

    public async Task<IReadOnlyList<MigrationJobDto>> ListJobsAsync(CancellationToken ct = default)
    {
        var jobs = await db.MigrationJobs.AsNoTracking().OrderByDescending(j => j.CreatedAtUtc).Take(20).ToListAsync(ct);
        return jobs.Select(ToJobDto).ToList();
    }

    public async Task<IReadOnlyList<MigrationErrorDto>> ListErrorsAsync(long jobId, CancellationToken ct = default)
        => await db.MigrationErrors.AsNoTracking().Where(e => e.MigrationJobId == jobId)
            .OrderBy(e => e.Id)
            .Select(e => new MigrationErrorDto(e.Id, e.MigrationJobId, e.EntityType, e.PaymoId, e.Message, e.RetryCount, e.CreatedAtUtc))
            .ToListAsync(ct);

    private static MigrationJobDto ToJobDto(MigrationJob j) => new(
        j.Id, j.Type, j.Status, j.TotalRecords, j.ProcessedRecords, j.ErrorCount, j.StartedUtc, j.FinishedUtc, j.Message);
}
