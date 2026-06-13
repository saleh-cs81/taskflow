using System.Security.Cryptography;
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
    ITenantContext tenant,
    IPasswordHasher hasher,
    IMigrationQueue queue,
    IDateTime clock,
    ILogger<MigrationService> logger) : IMigrationService
{
    // Fallback owner for imported time entries when the Paymo user can't be mapped.
    private long _runUserId;

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
        if (!await paymo.ValidateKeyAsync(r.ApiKey, ct)) throw new ValidationAppException("integration.invalid_key");

        var conn = await db.PaymoConnections.FirstOrDefaultAsync(ct);
        if (conn is null) { conn = new PaymoConnection(); db.PaymoConnections.Add(conn); }
        conn.ApiKeyEncrypted = protector.Protect(r.ApiKey);
        conn.Status = PaymoConnectionStatus.Connected;
        await db.SaveChangesAsync(ct);
        return new PaymoConnectionDto(true, conn.Status, conn.LastSyncUtc);
    }

    public async Task<StartMigrationResult> StartAsync(MigrationJobType type, CancellationToken ct = default)
    {
        var conn = await db.PaymoConnections.FirstOrDefaultAsync(ct)
            ?? throw new ValidationAppException("integration.not_connected");
        var tenantId = tenant.TenantId ?? throw new UnauthorizedAppException("error.unauthorized");
        var userId = currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");

        // Supersede any stale Pending/Running jobs so a crashed run never blocks re-running.
        var stale = await db.MigrationJobs
            .Where(j => j.Status == MigrationJobStatus.Pending || j.Status == MigrationJobStatus.Running)
            .ToListAsync(ct);
        foreach (var s in stale)
        {
            s.Status = MigrationJobStatus.Failed;
            s.FinishedUtc = clock.UtcNow;
            s.Message = "Superseded by a new run.";
        }

        var job = new MigrationJob { Type = type, Status = MigrationJobStatus.Pending };
        db.MigrationJobs.Add(job);
        await db.SaveChangesAsync(ct);

        queue.Enqueue(new MigrationWorkItem(job.Id, tenantId, userId, type));
        return new StartMigrationResult(job.Id);
    }

    // ---- Background execution (no HttpContext) ----
    public async Task ExecuteAsync(MigrationWorkItem item, CancellationToken ct = default)
    {
        tenant.SetTenant(item.TenantId);
        _runUserId = item.UserId;

        var job = await db.MigrationJobs.FirstOrDefaultAsync(j => j.Id == item.JobId, ct);
        if (job is null) return;

        var conn = await db.PaymoConnections.FirstOrDefaultAsync(ct);
        if (conn is null) { job.Status = MigrationJobStatus.Failed; job.Message = "Not connected."; await db.SaveChangesAsync(ct); return; }

        job.Status = MigrationJobStatus.Running;
        job.StartedUtc = clock.UtcNow;
        job.Message = "Starting…";
        await db.SaveChangesAsync(ct);

        try
        {
            string apiKey;
            try { apiKey = protector.Unprotect(conn.ApiKeyEncrypted); }
            catch
            {
                // Stored key can't be decrypted (e.g. data-protection ring changed). Force reconnect.
                conn.Status = PaymoConnectionStatus.Invalid;
                throw new InvalidOperationException("Stored Paymo key could not be read. Please reconnect your API key and run again.");
            }
            var since = item.Type == MigrationJobType.Incremental ? conn.LastSyncUtc : null;

            // 1) Users first, so tasks/time entries can reference real people.
            var userMap = new Dictionary<long, long>();
            foreach (var u in await paymo.GetUsersAsync(apiKey, ct))
            {
                try { userMap[u.Id] = await UpsertUserAsync(u, ct); job.ProcessedRecords++; }
                catch (Exception ex) { await RecordError(job, "User", u.Id, ex, u, ct); }
            }
            job.Message = $"Imported {userMap.Count} users…";
            await db.SaveChangesAsync(ct);

            // 2) Projects (outer loop drives the progress bar).
            var projects = await paymo.GetProjectsAsync(apiKey, since, ct);
            job.ProjectsTotal = projects.Count;
            await db.SaveChangesAsync(ct);

            foreach (var p in projects)
            {
                long localProjectId;
                try { localProjectId = await UpsertProjectAsync(p, ct); job.ProcessedRecords++; }
                catch (Exception ex) { await RecordError(job, "Project", p.Id, ex, p, ct); job.ProjectsDone++; await db.SaveChangesAsync(ct); continue; }

                await ImportListsTasksTime(job, apiKey, p.Id, localProjectId, since, userMap, ct);

                job.ProjectsDone++;
                job.Message = $"Imported {job.ProjectsDone}/{job.ProjectsTotal} projects · {job.ProcessedRecords} records";
                await db.SaveChangesAsync(ct);   // persist progress per project for live polling
            }

            job.TotalRecords = job.ProcessedRecords + job.ErrorCount;
            job.Status = job.ErrorCount > 0 ? MigrationJobStatus.CompletedWithErrors : MigrationJobStatus.Completed;
            job.Message = $"Imported {job.ProcessedRecords} records across {job.ProjectsDone} projects ({job.ErrorCount} errors).";
            conn.LastSyncUtc = clock.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Paymo migration job {JobId} failed", job.Id);
            job.Status = MigrationJobStatus.Failed;
            job.Message = "Failed: " + ex.Message + " — re-run to resume from where it stopped.";
        }

        job.FinishedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportListsTasksTime(MigrationJob job, string apiKey, long paymoProjectId, long localProjectId,
        DateTime? since, IReadOnlyDictionary<long, long> userMap, CancellationToken ct)
    {
        var listMap = new Dictionary<long, long>();
        foreach (var l in await paymo.GetTaskListsAsync(apiKey, paymoProjectId, ct))
        {
            try { listMap[l.Id] = await UpsertTaskListAsync(l, localProjectId, ct); job.ProcessedRecords++; }
            catch (Exception ex) { await RecordError(job, "TaskList", l.Id, ex, l, ct); }
        }

        var taskMap = new Dictionary<long, long>();
        foreach (var t in await paymo.GetTasksAsync(apiKey, paymoProjectId, since, ct))
        {
            try
            {
                long? localList = t.TaskListId is { } lid && listMap.TryGetValue(lid, out var ll) ? ll : null;
                long? assignee = t.AssigneeUserId is { } au && userMap.TryGetValue(au, out var lu) ? lu : null;
                taskMap[t.Id] = await UpsertTaskAsync(t, localProjectId, localList, assignee, ct);
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "Task", t.Id, ex, t, ct); }
        }

        foreach (var e in await paymo.GetTimeEntriesAsync(apiKey, paymoProjectId, since, ct))
        {
            try
            {
                long? localTask = e.TaskId is { } tid && taskMap.TryGetValue(tid, out var lt) ? lt : null;
                long owner = e.UserId is { } eu && userMap.TryGetValue(eu, out var lo) ? lo : _runUserId;
                await UpsertTimeEntryAsync(e, localProjectId, localTask, owner, ct);
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "TimeEntry", e.Id, ex, e, ct); }
        }
    }

    // --- Idempotent upserts keyed by EntityMapping(EntityType, PaymoId) ---

    private async Task<long> UpsertUserAsync(PaymoUser u, CancellationToken ct)
    {
        var existing = await FindMappingAsync("User", u.Id, ct);
        if (existing is not null)
        {
            var usr = await db.Users.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (usr is not null) { usr.FullName = u.Name; usr.IsActive = u.Active; }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }

        var email = string.IsNullOrWhiteSpace(u.Email) ? $"paymo-{u.Id}@import.local" : u.Email.Trim();
        var normalized = email.ToUpperInvariant();

        // Link to an existing user with the same email instead of creating a duplicate.
        var match = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.NormalizedEmail == normalized, ct);
        if (match is not null)
        {
            await AddMappingAsync("User", u.Id, match.Id, ct);
            return match.Id;
        }

        var created = new User
        {
            Email = email, NormalizedEmail = normalized, FullName = string.IsNullOrWhiteSpace(u.Name) ? email : u.Name,
            PasswordHash = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))),
            IsActive = u.Active, EmailConfirmed = true, Locale = "en"
        };
        db.Users.Add(created);
        await db.SaveChangesAsync(ct);

        var employee = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "EMPLOYEE", ct);
        if (employee is not null) { db.UserRoles.Add(new UserRole { UserId = created.Id, RoleId = employee.Id }); await db.SaveChangesAsync(ct); }

        await AddMappingAsync("User", u.Id, created.Id, ct);
        return created.Id;
    }

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

    private async Task<long> UpsertTaskAsync(PaymoTask t, long localProjectId, long? localListId, long? assigneeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) throw new InvalidOperationException("Task title is required.");
        var status = t.Complete ? WorkStatus.Done : WorkStatus.Todo;
        var priority = MapPriority(t.Priority);
        var existing = await FindMappingAsync("Task", t.Id, ct);
        if (existing is not null)
        {
            var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (task is not null)
            {
                task.Title = t.Name; task.Description = t.Description; task.TaskListId = localListId;
                task.Status = status; task.Priority = priority; task.DueDate = t.DueDate; task.AssigneeId = assigneeId;
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
            Status = status, Priority = priority, DueDate = t.DueDate, AssigneeId = assigneeId, Position = pos,
            CompletedAtUtc = t.Complete ? clock.UtcNow : null
        };
        db.Tasks.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Task", t.Id, created.Id, ct);
        return created.Id;
    }

    private async Task UpsertTimeEntryAsync(PaymoTimeEntry e, long localProjectId, long? localTaskId, long ownerUserId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("TimeEntry", e.Id, ct);
        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return; }

        var created = new TimeEntry
        {
            UserId = ownerUserId, ProjectId = localProjectId, TaskId = localTaskId,
            StartUtc = e.Start, EndUtc = e.End, DurationSeconds = e.DurationSeconds,
            IsRunning = false, IsBillable = e.Billable, Note = e.Description, Source = TimeEntrySource.Manual
        };
        db.TimeEntries.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("TimeEntry", e.Id, created.Id, ct);
    }

    // Paymo priority is 100/75/50/25 (higher = more important).
    private static TaskPriority MapPriority(int p) => p switch
    {
        >= 100 => TaskPriority.Urgent,
        >= 75 => TaskPriority.High,
        >= 50 => TaskPriority.Normal,
        >= 25 => TaskPriority.Low,
        _ => TaskPriority.Normal
    };

    public async Task ResetAsync(CancellationToken ct = default)
    {
        var maps = await db.EntityMappings.IgnoreQueryFilters()
            .Where(m => tenant.IsSuperAdmin || m.TenantId == tenant.TenantId)
            .Select(m => new { m.EntityType, m.LocalId }).ToListAsync(ct);

        long[] Ids(string type) => maps.Where(m => m.EntityType == type).Select(m => m.LocalId).Distinct().ToArray();

        // Child → parent order; ExecuteDelete is a hard delete (bypasses soft-delete interceptor).
        var entryIds = Ids("TimeEntry");
        var taskIds = Ids("Task");
        var listIds = Ids("TaskList");
        var projIds = Ids("Project");
        var userIds = Ids("User");

        if (entryIds.Length > 0) await db.TimeEntries.IgnoreQueryFilters().Where(x => entryIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (taskIds.Length > 0) await db.Tasks.IgnoreQueryFilters().Where(x => taskIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (listIds.Length > 0) await db.TaskLists.IgnoreQueryFilters().Where(x => listIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (projIds.Length > 0) await db.Projects.IgnoreQueryFilters().Where(x => projIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (userIds.Length > 0) await db.Users.IgnoreQueryFilters().Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync(ct);

        var tid = tenant.TenantId;
        await db.EntityMappings.IgnoreQueryFilters().Where(m => tenant.IsSuperAdmin || m.TenantId == tid).ExecuteDeleteAsync(ct);
        await db.MigrationErrors.IgnoreQueryFilters().Where(m => tenant.IsSuperAdmin || m.TenantId == tid).ExecuteDeleteAsync(ct);
        await db.MigrationJobs.IgnoreQueryFilters().Where(m => tenant.IsSuperAdmin || m.TenantId == tid).ExecuteDeleteAsync(ct);

        var conn = await db.PaymoConnections.FirstOrDefaultAsync(ct);
        if (conn is not null) { conn.LastSyncUtc = null; await db.SaveChangesAsync(ct); }
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
            Message = ex.Message, PayloadJson = SafeJson(payload), RetryCount = 0
        });
        job.ErrorCount++;
        await db.SaveChangesAsync(ct);
    }

    private static string? SafeJson(object o) { try { return JsonSerializer.Serialize(o); } catch { return null; } }

    public async Task<MigrationJobDto> GetJobAsync(long jobId, CancellationToken ct = default)
    {
        var job = await db.MigrationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        return ToJobDto(job);
    }

    public async Task<MigrationJobDto?> GetLatestJobAsync(CancellationToken ct = default)
    {
        var job = await db.MigrationJobs.AsNoTracking().OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct);
        return job is null ? null : ToJobDto(job);
    }

    public async Task<IReadOnlyList<MigrationJobDto>> ListJobsAsync(CancellationToken ct = default)
    {
        var jobs = await db.MigrationJobs.AsNoTracking().OrderByDescending(j => j.Id).Take(20).ToListAsync(ct);
        return jobs.Select(ToJobDto).ToList();
    }

    public async Task<IReadOnlyList<MigrationErrorDto>> ListErrorsAsync(long jobId, CancellationToken ct = default)
        => await db.MigrationErrors.AsNoTracking().Where(e => e.MigrationJobId == jobId)
            .OrderBy(e => e.Id)
            .Select(e => new MigrationErrorDto(e.Id, e.MigrationJobId, e.EntityType, e.PaymoId, e.Message, e.RetryCount, e.CreatedAtUtc))
            .ToListAsync(ct);

    private static MigrationJobDto ToJobDto(MigrationJob j) => new(
        j.Id, j.Type, j.Status, j.TotalRecords, j.ProcessedRecords, j.ErrorCount,
        j.ProjectsTotal, j.ProjectsDone, j.StartedUtc, j.FinishedUtc, j.Message);
}
