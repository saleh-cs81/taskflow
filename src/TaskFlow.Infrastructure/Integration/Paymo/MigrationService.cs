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
    IFileStorage fileStorage,
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

    public Task<StartMigrationResult> StartAsync(MigrationJobType type, CancellationToken ct = default)
        => QueueRunAsync(type, null, MigrationMode.Full, null, ct);

    // Staged migration: import only the next `count` not-yet-imported projects (count <= 0 => all).
    public Task<StartMigrationResult> StartBatchAsync(int count, CancellationToken ct = default)
        => QueueRunAsync(MigrationJobType.Full, count > 0 ? count : null, MigrationMode.Batch, null, ct);

    // Migrate base/shared data only (users, clients, contacts, statuses, client-level financials).
    public Task<StartMigrationResult> StartUsersAsync(CancellationToken ct = default)
        => QueueRunAsync(MigrationJobType.Full, null, MigrationMode.Users, null, ct);

    // Migrate ONE project, fully (self-contained).
    public Task<StartMigrationResult> StartProjectAsync(long paymoProjectId, CancellationToken ct = default)
        => QueueRunAsync(MigrationJobType.Full, null, MigrationMode.Project, paymoProjectId, ct);

    // Request a graceful stop of the currently running migration (checked between projects).
    public async Task StopAsync(CancellationToken ct = default)
    {
        var running = await db.MigrationJobs
            .Where(j => j.Status == MigrationJobStatus.Pending || j.Status == MigrationJobStatus.Running)
            .ToListAsync(ct);
        foreach (var j in running) j.CancelRequested = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task<StartMigrationResult> QueueRunAsync(MigrationJobType type, int? batchSize, MigrationMode mode, long? targetProjectId, CancellationToken ct)
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

        var job = new MigrationJob { Type = type, Status = MigrationJobStatus.Pending, BatchSize = batchSize, Mode = mode, TargetPaymoProjectId = targetProjectId };
        db.MigrationJobs.Add(job);
        await db.SaveChangesAsync(ct);

        queue.Enqueue(new MigrationWorkItem(job.Id, tenantId, userId, type, batchSize, mode, targetProjectId));
        return new StartMigrationResult(job.Id);
    }

    public async Task<MigrationCatalogDto> GetProjectItemsAsync(CancellationToken ct = default)
    {
        var items = await db.MigrationProjectItems.AsNoTracking().OrderBy(i => i.Seq)
            .Select(i => new MigrationProjectItemDto(
                i.PaymoProjectId, i.Name, i.Status, i.RecordCount, i.ErrorMessage, i.ImportedAtUtc))
            .ToListAsync(ct);
        var imported = items.Count(i => i.Status == MigrationProjectStatus.Imported);
        var failed = items.Count(i => i.Status == MigrationProjectStatus.Failed);
        var pending = items.Count - imported - failed;
        return new MigrationCatalogDto(items.Count, imported, failed, pending, items);
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
        // Counters are recomputed each run, so reset them on (re)start to keep progress accurate on resume.
        job.ProcessedRecords = 0;
        job.ProjectsDone = 0;
        job.ErrorCount = 0;
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

            // Base reference + prerequisite data (users, clients, contacts, statuses), gated to run once.
            var (userMap, clientMap, statusMap, wfStatuses) = await EnsureBaseDataAsync(job, apiKey, since, ct);

            var ctx = new ImportContext(userMap);
            foreach (var kv in wfStatuses) ctx.WorkflowStatus[kv.Key] = kv.Value;
            await RehydrateContextAsync(ctx, ct);

            // Refresh the per-project catalog so the staging page lists everything.
            var projects = await SafeFetchAsync(job, "Project", 0, () => paymo.GetProjectsAsync(apiKey, since, ct), ct);
            await SyncCatalogAsync(projects, ct);
            var projById = projects.ToDictionary(p => p.Id);

            var allItems = await db.MigrationProjectItems.OrderBy(i => i.Seq).ToListAsync(ct);
            job.ProjectsTotal = allItems.Count;
            job.ProjectsDone = allItems.Count(i => i.Status == MigrationProjectStatus.Imported);
            await db.SaveChangesAsync(ct);

            if (item.Mode == MigrationMode.Users)
            {
                // "Migrate users" page: base data (done above) + client-level financials. No projects.
                await ImportFinancialsAsync(job, apiKey, clientMap, ctx, since, ct);
                job.Message = $"Base data ready · {userMap.Count} users, {clientMap.Count} clients. Now migrate projects one by one.";
            }
            else
            {
                // Select projects to import: one specific (Project), the next N pending (Batch), or all pending.
                List<MigrationProjectItem> toImport = item.Mode == MigrationMode.Project
                    ? allItems.Where(i => i.PaymoProjectId == item.TargetProjectId && i.Status != MigrationProjectStatus.Imported).ToList()
                    : (item.BatchSize is { } n && n > 0
                        ? allItems.Where(i => i.Status == MigrationProjectStatus.Pending).Take(n).ToList()
                        : allItems.Where(i => i.Status == MigrationProjectStatus.Pending).ToList());

                if (toImport.Count > 0)
                {
                    // Milestones can't be filtered by project_id — fetch all once per run, slice per project.
                    var milestonesByProject = new Dictionary<long, List<PaymoMilestone>>();
                    foreach (var m in await SafeFetchAsync(job, "Milestone", 0, () => paymo.GetMilestonesAsync(apiKey, ct), ct))
                    { if (!milestonesByProject.TryGetValue(m.ProjectId, out var l)) { l = new(); milestonesByProject[m.ProjectId] = l; } l.Add(m); }

                    foreach (var pItem in toImport)
                    {
                        if (await IsCancelledAsync(job, ct)) { job.Message = "Stopped by user."; break; }
                        if (!projById.TryGetValue(pItem.PaymoProjectId, out var p)) continue;   // removed from Paymo
                        pItem.Status = MigrationProjectStatus.Importing;
                        job.Message = $"Importing \"{pItem.Name}\"…";
                        await db.SaveChangesAsync(ct);

                        var before = job.ProcessedRecords;
                        var errBefore = job.ErrorCount;
                        try
                        {
                            var localProjectId = await UpsertProjectAsync(p, clientMap, statusMap, ct); job.ProcessedRecords++;
                            ctx.ProjectByPaymo[p.Id] = localProjectId;
                            var projMs = milestonesByProject.TryGetValue(p.Id, out var ms) ? ms : (IReadOnlyList<PaymoMilestone>)[];
                            await ImportProjectFullAsync(job, apiKey, p, localProjectId, since, ctx, projMs, clientMap, ct);
                            pItem.LocalProjectId = localProjectId;
                            pItem.RecordCount = job.ProcessedRecords - before;
                            // Imported even if a few sub-items had issues (e.g. an undownloadable file); note them.
                            var newErrors = job.ErrorCount - errBefore;
                            pItem.Status = MigrationProjectStatus.Imported;
                            pItem.ErrorMessage = newErrors > 0 ? $"Imported with {newErrors} skipped item(s) — see job errors." : null;
                            pItem.ImportedAtUtc = clock.UtcNow;
                            job.ProjectsDone++;
                        }
                        catch (Exception ex)
                        {
                            await RecordError(job, "Project", p.Id, ex, p, ct);
                            pItem.Status = MigrationProjectStatus.Failed;
                            pItem.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                        }
                        await db.SaveChangesAsync(ct);
                    }
                }

                job.ProjectsDone = await db.MigrationProjectItems.CountAsync(i => i.Status == MigrationProjectStatus.Imported, ct);
                var left = job.ProjectsTotal - job.ProjectsDone;
                job.Message = job.CancelRequested
                    ? $"Stopped · {job.ProjectsDone}/{job.ProjectsTotal} projects imported."
                    : item.Mode == MigrationMode.Project
                        ? (toImport.Count == 0 ? "Already imported." : $"Project imported · {job.ProjectsDone}/{job.ProjectsTotal} total.")
                        : (left > 0 ? $"Batch done · {job.ProjectsDone}/{job.ProjectsTotal} imported ({left} remaining)." : $"All {job.ProjectsTotal} projects imported.");
            }

            job.TotalRecords = job.ProcessedRecords + job.ErrorCount;
            job.Status = job.ErrorCount > 0 ? MigrationJobStatus.CompletedWithErrors : MigrationJobStatus.Completed;
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

    // Cross-project maps so globally-fetched comments/files can be anchored.
    private sealed class ImportContext(IReadOnlyDictionary<long, long> userMap)
    {
        public IReadOnlyDictionary<long, long> UserMap { get; } = userMap;
        public Dictionary<long, long> ProjectByPaymo { get; } = new();
        public Dictionary<long, long> TaskByPaymo { get; } = new();
        public Dictionary<long, long> DiscussionByPaymo { get; } = new();
        public Dictionary<long, long> ThreadToTask { get; } = new();        // paymo thread_id -> local task id
        public Dictionary<long, long> ThreadToDiscussion { get; } = new();  // paymo thread_id -> local discussion id
        public Dictionary<long, long> CommentByPaymo { get; } = new();      // paymo comment id -> local Comment id
        public Dictionary<long, PaymoWorkflowStatus> WorkflowStatus { get; } = new(); // paymo status_id -> workflow status
    }

    // Base/reference + prerequisite data (statuses, users, clients, contacts). Users/clients import once
    // (gated by PrereqDone) and are reused as id maps on later runs.
    private async Task<(Dictionary<long, long> userMap, Dictionary<long, long> clientMap, Dictionary<long, string> statusMap, Dictionary<long, PaymoWorkflowStatus> wfStatuses)>
        EnsureBaseDataAsync(MigrationJob job, string apiKey, DateTime? since, CancellationToken ct)
    {
        var statusMap = new Dictionary<long, string>();
        try { foreach (var s in await paymo.GetProjectStatusesAsync(apiKey, ct)) statusMap[s.Id] = s.Name; }
        catch (Exception ex) { await RecordError(job, "ProjectStatus", 0, ex, "statuses", ct); }
        var wfStatuses = new Dictionary<long, PaymoWorkflowStatus>();
        try { foreach (var ws in await paymo.GetWorkflowStatusesAsync(apiKey, ct)) wfStatuses[ws.Id] = ws; }
        catch (Exception ex) { await RecordError(job, "WorkflowStatus", 0, ex, "workflowstatuses", ct); }

        Dictionary<long, long> userMap, clientMap;
        if (await FindMappingAsync("PrereqDone", 0, ct) is null)
        {
            userMap = new();
            foreach (var u in await SafeFetchAsync(job, "User", 0, () => paymo.GetUsersAsync(apiKey, ct), ct))
            { try { userMap[u.Id] = await UpsertUserAsync(u, ct); job.ProcessedRecords++; } catch (Exception ex) { await RecordError(job, "User", u.Id, ex, u, ct); } }
            clientMap = new();
            foreach (var c in await SafeFetchAsync(job, "Client", 0, () => paymo.GetClientsAsync(apiKey, since, ct), ct))
            { try { clientMap[c.Id] = await UpsertClientAsync(c, ct); job.ProcessedRecords++; } catch (Exception ex) { await RecordError(job, "Client", c.Id, ex, c, ct); } }
            try
            {
                foreach (var cc in await paymo.GetClientContactsAsync(apiKey, ct))
                {
                    if (!clientMap.TryGetValue(cc.ClientId, out var lc)) continue;
                    try { await UpsertClientContactAsync(cc, lc, ct); job.ProcessedRecords++; } catch (Exception ex) { await RecordError(job, "ClientContact", cc.Id, ex, cc, ct); }
                }
            }
            catch (Exception ex) { await RecordError(job, "ClientContact", 0, ex, "contacts", ct); }
            await EnsureMappingAsync("PrereqDone", 0, 0, ct);
        }
        else { userMap = await LoadIdMapAsync("User", ct); clientMap = await LoadIdMapAsync("Client", ct); }
        await db.SaveChangesAsync(ct);
        return (userMap, clientMap, statusMap, wfStatuses);
    }

    // True if Stop was requested for this job (set on a different request/context, so read fresh).
    private async Task<bool> IsCancelledAsync(MigrationJob job, CancellationToken ct)
        => await db.MigrationJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.CancelRequested).FirstOrDefaultAsync(ct);

    // Imports ONE project's complete data, self-contained (no global passes): milestones, lists, tasks,
    // subtasks, discussions, time entries, comments (this project's threads only), files, bookings.
    private async Task ImportProjectFullAsync(MigrationJob job, string apiKey, PaymoProject project, long localProjectId,
        DateTime? since, ImportContext ctx, IReadOnlyList<PaymoMilestone> milestones,
        IReadOnlyDictionary<long, long> clientMap, CancellationToken ct)
    {
        var paymoProjectId = project.Id;
        var userMap = ctx.UserMap;
        var projectTaskPaymoIds = new List<long>();   // for subtasks + booking resolution
        var projectThreads = new List<long>();         // for this project's comments

        // Milestones (project-level, supplied from the per-run fetch), so tasks can link through their task list.
        var milestoneMap = new Dictionary<long, long>();
        foreach (var m in milestones)
        {
            try { milestoneMap[m.Id] = await UpsertMilestoneAsync(m, localProjectId, ct); job.ProcessedRecords++; }
            catch (Exception ex) { await RecordError(job, "Milestone", m.Id, ex, m, ct); }
        }

        // Task lists (capture each list's milestone link for task-level wiring).
        var listMap = new Dictionary<long, long>();
        var listMilestone = new Dictionary<long, long>(); // paymo list id -> paymo milestone id
        foreach (var l in await SafeFetchAsync(job, "TaskList", paymoProjectId, () => paymo.GetTaskListsAsync(apiKey, paymoProjectId, ct), ct))
        {
            try
            {
                listMap[l.Id] = await UpsertTaskListAsync(l, localProjectId, ct);
                if (l.MilestoneId is { } mid) listMilestone[l.Id] = mid;
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "TaskList", l.Id, ex, l, ct); }
        }

        // Tasks (+ multi-assignee + milestone linkage via their list).
        foreach (var t in await SafeFetchAsync(job, "Task", paymoProjectId, () => paymo.GetTasksAsync(apiKey, paymoProjectId, since, ct), ct))
        {
            try
            {
                long? localList = t.TaskListId is { } lid && listMap.TryGetValue(lid, out var ll) ? ll : null;
                long? localMilestone = t.TaskListId is { } lid2 && listMilestone.TryGetValue(lid2, out var pm)
                    && milestoneMap.TryGetValue(pm, out var lm) ? lm : null;
                var assignees = t.AssigneeUserIds.Where(userMap.ContainsKey).Select(a => userMap[a]).ToList();
                var localTaskId = await UpsertTaskAsync(t, localProjectId, localList, localMilestone, assignees, ctx.WorkflowStatus, ct);
                ctx.TaskByPaymo[t.Id] = localTaskId;
                projectTaskPaymoIds.Add(t.Id);
                if (t.ThreadId is { } th) { ctx.ThreadToTask[th] = localTaskId; projectThreads.Add(th); await EnsureMappingAsync("TaskThread", th, localTaskId, ct); }
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "Task", t.Id, ex, t, ct); }
        }

        // Discussions (+ description as first post).
        foreach (var d in await SafeFetchAsync(job, "Discussion", paymoProjectId, () => paymo.GetDiscussionsAsync(apiKey, paymoProjectId, ct), ct))
        {
            try
            {
                long dAuthor = d.UserId is { } du && userMap.TryGetValue(du, out var dl) ? dl : _runUserId;
                var localDisc = await UpsertDiscussionAsync(d, localProjectId, dAuthor, ct);
                ctx.DiscussionByPaymo[d.Id] = localDisc;
                if (d.ThreadId is { } th) { ctx.ThreadToDiscussion[th] = localDisc; projectThreads.Add(th); await EnsureMappingAsync("DiscThread", th, localDisc, ct); }
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "Discussion", d.Id, ex, d, ct); }
        }

        // Time entries.
        foreach (var e in await SafeFetchAsync(job, "TimeEntry", paymoProjectId, () => paymo.GetTimeEntriesAsync(apiKey, paymoProjectId, since, ct), ct))
        {
            try
            {
                long? localTask = e.TaskId is { } tid && ctx.TaskByPaymo.TryGetValue(tid, out var lt) ? lt : null;
                long owner = e.UserId is { } eu && userMap.TryGetValue(eu, out var lo) ? lo : _runUserId;
                await UpsertTimeEntryAsync(e, localProjectId, localTask, owner, ct);
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "TimeEntry", e.Id, ex, e, ct); }
        }

        // Subtasks -> checklist items, fetched per task (Paymo requires a task_id filter on subtasks).
        foreach (var ptid in projectTaskPaymoIds)
        {
            if (!ctx.TaskByPaymo.TryGetValue(ptid, out var localTask)) continue;
            foreach (var s in await SafeFetchAsync(job, "Subtask", ptid, () => paymo.GetSubtasksByTaskAsync(apiKey, ptid, ct), ct))
            {
                try { await UpsertSubtaskAsync(s, localTask, ct); job.ProcessedRecords++; }
                catch (Exception ex) { await RecordError(job, "Subtask", s.Id, ex, s, ct); }
            }
        }

        // Comments — only this project's threads (task + discussion). Anchored to Comment or DiscussionPost.
        foreach (var threadId in projectThreads.Distinct())
        {
            foreach (var c in await SafeFetchAsync(job, "Comment", threadId, () => paymo.GetCommentsAsync(apiKey, threadId, ct), ct))
            {
                try
                {
                    long author = c.UserId is { } u && userMap.TryGetValue(u, out var lu) ? lu : _runUserId;
                    if (c.ThreadId is { } th && ctx.ThreadToTask.TryGetValue(th, out var localTask))
                    {
                        var local = await UpsertCommentAsync(c, localTask, author, ct);
                        if (local is { } lid) ctx.CommentByPaymo[c.Id] = lid;
                        job.ProcessedRecords++;
                    }
                    else if (c.ThreadId is { } th2 && ctx.ThreadToDiscussion.TryGetValue(th2, out var localDisc))
                    {
                        await UpsertDiscussionPostAsync(c, localDisc, author, ct);
                        job.ProcessedRecords++;
                    }
                }
                catch (Exception ex) { await RecordError(job, "Comment", c.Id, ex, c, ct); }
            }
        }

        // Files for this project (anchored Task > Comment > Project).
        foreach (var f in await SafeFetchAsync(job, "File", paymoProjectId, () => paymo.GetFilesAsync(apiKey, paymoProjectId, ct), ct))
        {
            try
            {
                AttachmentTargetType? targetType = null; long targetId = 0;
                if (f.TaskId is { } tid && ctx.TaskByPaymo.TryGetValue(tid, out var lt)) { targetType = AttachmentTargetType.Task; targetId = lt; }
                else if (f.CommentId is { } cid && ctx.CommentByPaymo.TryGetValue(cid, out var lc)) { targetType = AttachmentTargetType.Comment; targetId = lc; }
                else { targetType = AttachmentTargetType.Project; targetId = localProjectId; }
                await UpsertFileAsync(f, targetType.Value, targetId, apiKey, ct);
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "File", f.Id, ex, f, ct); }
        }

        // Bookings for this project. Resolve user_task_id via users_tasks (filtered per this project's tasks).
        var bookings = await SafeFetchAsync(job, "Booking", paymoProjectId, () => paymo.GetBookingsAsync(apiKey, paymoProjectId, ct), ct);
        if (bookings.Count > 0)
        {
            var userTaskMap = new Dictionary<long, (long User, long Task)>();
            foreach (var ptid in projectTaskPaymoIds)
                foreach (var ut in await SafeFetchAsync(job, "UserTask", ptid, () => paymo.GetUserTasksByTaskAsync(apiKey, ptid, ct), ct))
                    userTaskMap[ut.Id] = (ut.UserId, ut.TaskId);
            foreach (var bk in bookings)
            {
                try
                {
                    long? pu = bk.UserId; long? pt = bk.TaskId;
                    if ((pu is null || pt is null) && userTaskMap.TryGetValue(bk.UserTaskId, out var utp)) { pu ??= utp.User; pt ??= utp.Task; }
                    if (pu is not { } puid || !userMap.TryGetValue(puid, out var localUser)) continue;
                    long? localTask = pt is { } ptid2 && ctx.TaskByPaymo.TryGetValue(ptid2, out var ltk) ? ltk : null;
                    await UpsertBookingAsync(bk, localUser, localProjectId, localTask, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex) { await RecordError(job, "Booking", bk.Id, ex, bk, ct); }
            }
        }
    }

    // Subtasks can't be filtered by project_id; fetch all once and map to imported tasks by task_id.
    private async Task ImportSubtasksAsync(MigrationJob job, string apiKey, ImportContext ctx, CancellationToken ct)
    {
        foreach (var s in await SafeFetchAsync(job, "Subtask", 0, () => paymo.GetSubtasksAsync(apiKey, ct), ct))
        {
            if (!ctx.TaskByPaymo.TryGetValue(s.TaskId, out var localTask)) continue;   // subtask of an un-imported task
            try { await UpsertSubtaskAsync(s, localTask, ct); job.ProcessedRecords++; }
            catch (Exception ex) { await RecordError(job, "Subtask", s.Id, ex, s, ct); }
        }
        job.Message = $"Imported subtasks… {job.ProcessedRecords} records";
        await db.SaveChangesAsync(ct);
    }

    // Comments are polymorphic: anchor by thread_id to a task (-> Comment) or a discussion (-> DiscussionPost).
    // Paymo requires a mandatory thread_id filter, so fetch per imported thread (deduped across task+discussion threads).
    private async Task ImportCommentsAsync(MigrationJob job, string apiKey, ImportContext ctx, CancellationToken ct)
    {
        var threadIds = ctx.ThreadToTask.Keys.Concat(ctx.ThreadToDiscussion.Keys).Distinct().ToList();
        foreach (var threadId in threadIds)
        {
            if (await FindMappingAsync("ThreadDone", threadId, ct) is not null) continue;   // resume skip
            foreach (var c in await SafeFetchAsync(job, "Comment", threadId, () => paymo.GetCommentsAsync(apiKey, threadId, ct), ct))
            {
                try
                {
                    long author = c.UserId is { } u && ctx.UserMap.TryGetValue(u, out var lu) ? lu : _runUserId;
                    if (c.ThreadId is { } th && ctx.ThreadToTask.TryGetValue(th, out var localTask))
                    {
                        var local = await UpsertCommentAsync(c, localTask, author, ct);
                        if (local is { } lid) ctx.CommentByPaymo[c.Id] = lid;
                        job.ProcessedRecords++;
                    }
                    else if (c.ThreadId is { } th2 && ctx.ThreadToDiscussion.TryGetValue(th2, out var localDisc))
                    {
                        await UpsertDiscussionPostAsync(c, localDisc, author, ct);
                        job.ProcessedRecords++;
                    }
                    // else: comment on an un-imported thread — skipped.
                }
                catch (Exception ex) { await RecordError(job, "Comment", c.Id, ex, c, ct); }
            }
            await EnsureMappingAsync("ThreadDone", threadId, threadId, ct);   // mark thread for resume skip
        }
        job.Message = $"Imported comments… {job.ProcessedRecords} records";
        await db.SaveChangesAsync(ct);
    }

    // Files require a mandatory filter; fetch per imported project, then anchor each file (Task > Comment > Project).
    private async Task ImportFilesAsync(MigrationJob job, string apiKey, ImportContext ctx, CancellationToken ct)
    {
        foreach (var (paymoProjectId, _) in ctx.ProjectByPaymo)
        {
            if (await FindMappingAsync("ProjFilesDone", paymoProjectId, ct) is not null) continue;   // resume skip
            foreach (var f in await SafeFetchAsync(job, "File", paymoProjectId, () => paymo.GetFilesAsync(apiKey, paymoProjectId, ct), ct))
            {
                try
                {
                    // Resolve attachment target (Task > Comment > Project).
                    AttachmentTargetType? targetType = null; long targetId = 0;
                    if (f.TaskId is { } tid && ctx.TaskByPaymo.TryGetValue(tid, out var lt)) { targetType = AttachmentTargetType.Task; targetId = lt; }
                    else if (f.CommentId is { } cid && ctx.CommentByPaymo.TryGetValue(cid, out var lc)) { targetType = AttachmentTargetType.Comment; targetId = lc; }
                    else if (f.ProjectId is { } pid && ctx.ProjectByPaymo.TryGetValue(pid, out var lp)) { targetType = AttachmentTargetType.Project; targetId = lp; }
                    else if (f.DiscussionId is { } did && ctx.DiscussionByPaymo.TryGetValue(did, out _) && f.ProjectId is { } pid2 && ctx.ProjectByPaymo.TryGetValue(pid2, out var lp2)) { targetType = AttachmentTargetType.Project; targetId = lp2; }
                    if (targetType is null) continue; // no imported target to attach to

                    await UpsertFileAsync(f, targetType.Value, targetId, apiKey, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex) { await RecordError(job, "File", f.Id, ex, f, ct); }
            }
            await EnsureMappingAsync("ProjFilesDone", paymoProjectId, paymoProjectId, ct);   // mark project's files for resume skip
        }
        job.Message = $"Imported files… {job.ProcessedRecords} records";
        await db.SaveChangesAsync(ct);
    }

    // Financials are tenant-global in Paymo (not per-project): expenses, invoices, payments, estimates.
    private async Task ImportFinancialsAsync(MigrationJob job, string apiKey,
        IReadOnlyDictionary<long, long> clientMap, ImportContext ctx, DateTime? since, CancellationToken ct)
    {
        // Expenses (linked to a client and/or project when those were imported).
        try
        {
            foreach (var x in await paymo.GetExpensesAsync(apiKey, since, ct))
            {
                try
                {
                    long? localClient = x.ClientId is { } cid && clientMap.TryGetValue(cid, out var lc) ? lc : null;
                    long? localProject = x.ProjectId is { } pid && ctx.ProjectByPaymo.TryGetValue(pid, out var lp) ? lp : null;
                    await UpsertExpenseAsync(x, localClient, localProject, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex) { await RecordError(job, "Expense", x.Id, ex, x, ct); }
            }
        }
        catch (Exception ex) { await RecordError(job, "Expense", 0, ex, "expenses", ct); }

        // Invoices (anchor for payments below).
        var invoiceByPaymo = new Dictionary<long, long>();
        try
        {
            foreach (var inv in await paymo.GetInvoicesAsync(apiKey, since, ct))
            {
                try
                {
                    long? localClient = inv.ClientId is { } cid && clientMap.TryGetValue(cid, out var lc) ? lc : null;
                    invoiceByPaymo[inv.Id] = await UpsertInvoiceAsync(inv, localClient, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex) { await RecordError(job, "Invoice", inv.Id, ex, inv, ct); }
            }
        }
        catch (Exception ex) { await RecordError(job, "Invoice", 0, ex, "invoices", ct); }

        // Invoice payments (anchored to imported invoices).
        try
        {
            foreach (var pay in await paymo.GetInvoicePaymentsAsync(apiKey, ct))
            {
                if (!invoiceByPaymo.TryGetValue(pay.InvoiceId, out var localInvoice)) continue;
                try { await UpsertInvoicePaymentAsync(pay, localInvoice, ct); job.ProcessedRecords++; }
                catch (Exception ex) { await RecordError(job, "InvoicePayment", pay.Id, ex, pay, ct); }
            }
        }
        catch (Exception ex) { await RecordError(job, "InvoicePayment", 0, ex, "invoicepayments", ct); }

        // Estimates.
        try
        {
            foreach (var est in await paymo.GetEstimatesAsync(apiKey, since, ct))
            {
                try
                {
                    long? localClient = est.ClientId is { } cid && clientMap.TryGetValue(cid, out var lc) ? lc : null;
                    await UpsertEstimateAsync(est, localClient, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex) { await RecordError(job, "Estimate", est.Id, ex, est, ct); }
            }
        }
        catch (Exception ex) { await RecordError(job, "Estimate", 0, ex, "estimates", ct); }

        job.Message = $"Imported financials… {job.ProcessedRecords} records";
        await db.SaveChangesAsync(ct);
    }

    // Bookings reference a user_task_id (a users_tasks assignment row), not direct user/task ids.
    // Build that map once per run (one call per user), then anchor each booking to imported entities.
    private async Task ImportBookingsAsync(MigrationJob job, string apiKey, ImportContext ctx, CancellationToken ct)
    {
        // user_task_id -> (paymo user id, paymo task id)
        var userTaskMap = new Dictionary<long, (long User, long Task)>();
        foreach (var paymoUserId in ctx.UserMap.Keys)
        {
            try
            {
                foreach (var ut in await paymo.GetUserTasksAsync(apiKey, paymoUserId, ct))
                    userTaskMap[ut.Id] = (ut.UserId, ut.TaskId);
            }
            catch (Exception ex) { await RecordError(job, "UserTask", paymoUserId, ex, "userstasks", ct); }
        }

        foreach (var (paymoProjectId, localProjectId) in ctx.ProjectByPaymo)
        {
            if (await FindMappingAsync("ProjBookingsDone", paymoProjectId, ct) is not null) continue;   // resume skip
            IReadOnlyList<PaymoBooking> bookings;
            try { bookings = await paymo.GetBookingsAsync(apiKey, paymoProjectId, ct); }
            catch (Exception ex) { await RecordError(job, "Booking", paymoProjectId, ex, "bookings", ct); continue; }

            foreach (var bk in bookings)
            {
                try
                {
                    // Prefer direct ids if the response carried them; else resolve via the assignment map.
                    long? paymoUser = bk.UserId;
                    long? paymoTask = bk.TaskId;
                    if ((paymoUser is null || paymoTask is null) && userTaskMap.TryGetValue(bk.UserTaskId, out var ut))
                    {
                        paymoUser ??= ut.User;
                        paymoTask ??= ut.Task;
                    }

                    if (paymoUser is not { } pu || !ctx.UserMap.TryGetValue(pu, out var localUser))
                        continue;   // a booking with no resolvable user isn't meaningful — skip
                    long? localTask = paymoTask is { } pt && ctx.TaskByPaymo.TryGetValue(pt, out var lt) ? lt : null;

                    await UpsertBookingAsync(bk, localUser, localProjectId, localTask, ct);
                    job.ProcessedRecords++;
                }
                catch (Exception ex) { await RecordError(job, "Booking", bk.Id, ex, bk, ct); }
            }
            await EnsureMappingAsync("ProjBookingsDone", paymoProjectId, paymoProjectId, ct);   // mark for resume skip
        }
        job.Message = $"Imported bookings… {job.ProcessedRecords} records";
        await db.SaveChangesAsync(ct);
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

        // Map Paymo role (type) to the local system role instead of forcing Employee.
        var role = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == MapRole(u.Type), ct)
            ?? await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "EMPLOYEE", ct);
        if (role is not null) { db.UserRoles.Add(new UserRole { UserId = created.Id, RoleId = role.Id }); await db.SaveChangesAsync(ct); }

        await AddMappingAsync("User", u.Id, created.Id, ct);
        return created.Id;
    }

    private static string MapRole(string? paymoType)
    {
        var t = (paymoType ?? "").ToLowerInvariant();
        if (t.Contains("admin") || t.Contains("owner")) return "COMPANYADMIN";
        if (t.Contains("manager") || t.Contains("pm") || t.Contains("project")) return "PROJECTMANAGER";
        if (t.Contains("guest") || t.Contains("client")) return "CLIENT";
        return "EMPLOYEE";
    }

    private async Task<long> UpsertClientAsync(PaymoClient c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) throw new InvalidOperationException("Client name is required.");
        var existing = await FindMappingAsync("Client", c.Id, ct);
        Client client;
        if (existing is not null)
            client = await db.Clients.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct) ?? new Client();
        else
            client = new Client();

        client.Name = c.Name; client.ContactEmail = c.Email; client.Phone = c.Phone;
        client.Address = c.Address; client.City = c.City; client.Country = c.Country; client.Website = c.Website;

        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return existing.LocalId; }

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Client", c.Id, client.Id, ct);
        return client.Id;
    }

    private async Task UpsertClientContactAsync(PaymoClientContact cc, long localClientId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("ClientContact", cc.Id, ct);
        if (existing is not null)
        {
            var row = await db.ClientContacts.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (row is not null) { row.Name = cc.Name; row.Email = cc.Email; row.Phone = cc.Phone; row.Position = cc.Position; row.IsMain = cc.IsMain; }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var created = new ClientContact
        {
            ClientId = localClientId, Name = cc.Name, Email = cc.Email, Phone = cc.Phone, Position = cc.Position, IsMain = cc.IsMain
        };
        db.ClientContacts.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("ClientContact", cc.Id, created.Id, ct);
    }

    private async Task<long> UpsertMilestoneAsync(PaymoMilestone m, long localProjectId, CancellationToken ct)
    {
        var status = m.Complete ? WorkStatus.Done : WorkStatus.Todo;
        var existing = await FindMappingAsync("Milestone", m.Id, ct);
        if (existing is not null)
        {
            var row = await db.Milestones.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (row is not null) { row.Name = m.Name; row.DueDate = m.DueDate; row.Status = status; }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }
        var created = new Milestone { ProjectId = localProjectId, Name = m.Name, DueDate = m.DueDate, Status = status };
        db.Milestones.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Milestone", m.Id, created.Id, ct);
        return created.Id;
    }

    private async Task UpsertSubtaskAsync(PaymoSubtask s, long localTaskId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("Subtask", s.Id, ct);
        if (existing is not null)
        {
            var row = await db.ChecklistItems.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (row is not null) { row.Text = s.Name; row.IsDone = s.Complete; row.Position = s.Seq * 1000; }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var created = new ChecklistItem { TaskId = localTaskId, Text = s.Name, IsDone = s.Complete, Position = (s.Seq <= 0 ? 1 : s.Seq) * 1000 };
        db.ChecklistItems.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Subtask", s.Id, created.Id, ct);
    }

    private async Task<long> UpsertDiscussionAsync(PaymoDiscussion d, long localProjectId, long author, CancellationToken ct)
    {
        var existing = await FindMappingAsync("Discussion", d.Id, ct);
        if (existing is not null)
        {
            var row = await db.Discussions.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (row is not null) row.Title = d.Name;
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }
        var created = new Discussion { ProjectId = localProjectId, Title = d.Name };
        db.Discussions.Add(created);
        await db.SaveChangesAsync(ct);
        created.CreatedById = author;                              // restore real author (interceptor set it to null in bg)
        if (!string.IsNullOrWhiteSpace(d.Description))
            db.DiscussionPosts.Add(new DiscussionPost { DiscussionId = created.Id, AuthorId = author, Body = d.Description.Trim() });
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Discussion", d.Id, created.Id, ct);
        return created.Id;
    }

    private async Task<long?> UpsertCommentAsync(PaymoComment c, long localTaskId, long author, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Content)) return null;
        var existing = await FindMappingAsync("Comment", c.Id, ct);
        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return existing.LocalId; }
        var created = new Comment { TargetType = CommentTargetType.Task, TargetId = localTaskId, AuthorId = author, Body = c.Content.Trim() };
        db.Comments.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Comment", c.Id, created.Id, ct);
        return created.Id;
    }

    private async Task UpsertDiscussionPostAsync(PaymoComment c, long localDiscussionId, long author, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Content)) return;
        var existing = await FindMappingAsync("DiscussionPost", c.Id, ct);
        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return; }
        var created = new DiscussionPost { DiscussionId = localDiscussionId, AuthorId = author, Body = c.Content.Trim() };
        db.DiscussionPosts.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("DiscussionPost", c.Id, created.Id, ct);
    }

    private async Task UpsertFileAsync(PaymoFile f, AttachmentTargetType targetType, long targetId, string apiKey, CancellationToken ct)
    {
        var existing = await FindMappingAsync("File", f.Id, ct);
        if (existing is not null) { existing.LastSyncedUtc = clock.UtcNow; await db.SaveChangesAsync(ct); return; }

        var download = await paymo.GetFileBytesAsync(apiKey, f.DownloadUrl ?? "", ct)
            ?? throw new InvalidOperationException($"File {f.Id} ({f.FileName}) could not be downloaded from {f.DownloadUrl ?? "(no url)"}.");
        using var stream = new MemoryStream(download.Bytes);
        var stored = await fileStorage.SaveAsync(stream, f.FileName, tenant.TenantId ?? 0, ct);

        var created = new FileObject
        {
            TargetType = targetType, TargetId = targetId, FileName = f.FileName,
            ContentType = string.IsNullOrWhiteSpace(f.Mime) ? download.ContentType : f.Mime!,
            SizeBytes = stored.SizeBytes, StorageKey = stored.StorageKey, Version = 1
        };
        db.Files.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("File", f.Id, created.Id, ct);
    }

    private async Task<long> UpsertProjectAsync(PaymoProject p, IReadOnlyDictionary<long, long> clientMap,
        IReadOnlyDictionary<long, string> statusMap, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) throw new InvalidOperationException("Project name is required.");
        long? localClient = p.ClientId is { } cid && clientMap.TryGetValue(cid, out var lc) ? lc : null;
        var status = MapProjectStatus(p, statusMap);

        var existing = await FindMappingAsync("Project", p.Id, ct);
        if (existing is not null)
        {
            var proj = await db.Projects.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (proj is not null) ApplyProject(proj, p, localClient, status);
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }

        var created = new Project();
        ApplyProject(created, p, localClient, status);
        db.Projects.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Project", p.Id, created.Id, ct);
        return created.Id;
    }

    private static void ApplyProject(Project proj, PaymoProject p, long? localClient, ProjectStatus status)
    {
        proj.Name = p.Name; proj.Description = p.Description; proj.Status = status;
        proj.ClientId = localClient; proj.Code = p.Code; proj.Color = p.Color;
        proj.BudgetHours = p.BudgetHours; proj.IsBillable = p.Billable;
    }

    private static ProjectStatus MapProjectStatus(PaymoProject p, IReadOnlyDictionary<long, string> statusMap)
    {
        var name = (p.StatusId is { } sid && statusMap.TryGetValue(sid, out var n) ? n : "").ToLowerInvariant();
        if (name.Contains("complete")) return ProjectStatus.Completed;
        if (name.Contains("hold")) return ProjectStatus.OnHold;
        if (name.Contains("cancel")) return ProjectStatus.Cancelled;
        if (name.Contains("archiv")) return ProjectStatus.Archived;
        if (name.Contains("proposal") || name.Contains("plan")) return ProjectStatus.Planned;
        if (name.Contains("active")) return ProjectStatus.Active;
        return p.Active ? ProjectStatus.Active : ProjectStatus.Archived;   // fallback to the boolean
    }

    private async Task<long> UpsertTaskListAsync(PaymoTaskList l, long localProjectId, CancellationToken ct)
    {
        var pos = l.Seq > 0 ? l.Seq * 1000.0 : 1000;
        var existing = await FindMappingAsync("TaskList", l.Id, ct);
        if (existing is not null)
        {
            var row = await db.TaskLists.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (row is not null) { row.Name = l.Name; row.Position = pos; }   // refresh name + ordering on re-sync
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }
        var created = new TaskList { ProjectId = localProjectId, Name = l.Name, Position = pos };
        db.TaskLists.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("TaskList", l.Id, created.Id, ct);
        return created.Id;
    }

    private async Task<long> UpsertTaskAsync(PaymoTask t, long localProjectId, long? localListId, long? localMilestoneId,
        IReadOnlyList<long> assigneeIds, IReadOnlyDictionary<long, PaymoWorkflowStatus> wfStatus, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) throw new InvalidOperationException("Task title is required.");
        var status = MapWorkStatus(t, wfStatus);
        var priority = MapPriority(t.Priority);
        var primary = assigneeIds.Count > 0 ? assigneeIds[0] : (long?)null;
        var pos = t.Seq > 0 ? t.Seq * 1000.0 : 1000;
        // Use Paymo's real completion timestamp; only synthesize if complete but no date provided.
        DateTime? completedAt = t.Complete ? (t.CompletedOn ?? clock.UtcNow) : null;

        var existing = await FindMappingAsync("Task", t.Id, ct);
        long localId;
        if (existing is not null)
        {
            var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (task is not null)
            {
                task.Title = t.Name; task.Description = t.Description; task.TaskListId = localListId;
                task.MilestoneId = localMilestoneId; task.Status = status; task.Priority = priority;
                task.DueDate = t.DueDate; task.StartDate = t.StartDate; task.AssigneeId = primary;
                task.Position = pos; task.CompletedAtUtc = completedAt;
            }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            localId = existing.LocalId;
        }
        else
        {
            var created = new TaskItem
            {
                ProjectId = localProjectId, TaskListId = localListId, MilestoneId = localMilestoneId,
                Title = t.Name, Description = t.Description, Status = status, Priority = priority,
                DueDate = t.DueDate, StartDate = t.StartDate, AssigneeId = primary, Position = pos,
                CompletedAtUtc = completedAt
            };
            db.Tasks.Add(created);
            await db.SaveChangesAsync(ct);
            await AddMappingAsync("Task", t.Id, created.Id, ct);
            localId = created.Id;
        }

        await SyncAssigneesAsync(localId, assigneeIds, ct);
        return localId;
    }

    // Reconcile the many-to-many TaskAssignee set to exactly the imported assignees.
    private async Task SyncAssigneesAsync(long taskId, IReadOnlyList<long> assigneeIds, CancellationToken ct)
    {
        var current = await db.TaskAssignees.Where(a => a.TaskId == taskId).ToListAsync(ct);
        foreach (var stale in current.Where(a => !assigneeIds.Contains(a.UserId))) db.TaskAssignees.Remove(stale);
        foreach (var uid in assigneeIds.Where(uid => current.All(a => a.UserId != uid)))
            db.TaskAssignees.Add(new TaskAssignee { TaskId = taskId, UserId = uid });
        await db.SaveChangesAsync(ct);
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

    private async Task UpsertExpenseAsync(PaymoExpense x, long? localClientId, long? localProjectId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("Expense", x.Id, ct);
        if (existing is not null)
        {
            var row = await db.Expenses.FirstOrDefaultAsync(e => e.Id == existing.LocalId, ct);
            if (row is not null)
            {
                row.ClientId = localClientId; row.ProjectId = localProjectId; row.Amount = x.Amount;
                row.Currency = string.IsNullOrWhiteSpace(x.Currency) ? "USD" : x.Currency!;
                row.Date = x.Date ?? row.Date; row.Description = x.Notes;
            }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var created = new Expense
        {
            ClientId = localClientId, ProjectId = localProjectId, Amount = x.Amount,
            Currency = string.IsNullOrWhiteSpace(x.Currency) ? "USD" : x.Currency!,
            Date = x.Date ?? clock.UtcNow, Category = "Imported", Description = x.Notes
        };
        db.Expenses.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Expense", x.Id, created.Id, ct);
    }

    private async Task<long> UpsertInvoiceAsync(PaymoInvoice inv, long? localClientId, CancellationToken ct)
    {
        var status = MapInvoiceStatus(inv.Status);
        var existing = await FindMappingAsync("Invoice", inv.Id, ct);
        if (existing is not null)
        {
            var row = await db.Invoices.FirstOrDefaultAsync(i => i.Id == existing.LocalId, ct);
            if (row is not null) ApplyInvoice(row, inv, localClientId, status);
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return existing.LocalId;
        }
        var created = new Invoice();
        ApplyInvoice(created, inv, localClientId, status);
        db.Invoices.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Invoice", inv.Id, created.Id, ct);
        return created.Id;
    }

    private void ApplyInvoice(Invoice row, PaymoInvoice inv, long? localClientId, InvoiceStatus status)
    {
        row.Number = inv.Number; row.ClientId = localClientId; row.Status = status;
        row.IssueDate = inv.Date ?? clock.UtcNow; row.DueDate = inv.DueDate;
        row.Currency = string.IsNullOrWhiteSpace(inv.Currency) ? "USD" : inv.Currency!;
        row.Subtotal = inv.Subtotal; row.TaxAmount = inv.TaxAmount; row.Total = inv.Total;
        row.TaxRate = inv.Subtotal > 0 ? Math.Round(inv.TaxAmount / inv.Subtotal * 100m, 2) : 0m;
    }

    private async Task UpsertInvoicePaymentAsync(PaymoInvoicePayment pay, long localInvoiceId, CancellationToken ct)
    {
        var existing = await FindMappingAsync("InvoicePayment", pay.Id, ct);
        if (existing is not null)
        {
            var row = await db.InvoicePayments.FirstOrDefaultAsync(p => p.Id == existing.LocalId, ct);
            if (row is not null) { row.Amount = pay.Amount; row.Date = pay.Date ?? row.Date; row.Notes = pay.Notes; }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var created = new InvoicePayment
        {
            InvoiceId = localInvoiceId, Amount = pay.Amount, Date = pay.Date ?? clock.UtcNow, Notes = pay.Notes
        };
        db.InvoicePayments.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("InvoicePayment", pay.Id, created.Id, ct);
    }

    private async Task UpsertEstimateAsync(PaymoEstimate est, long? localClientId, CancellationToken ct)
    {
        var status = MapEstimateStatus(est.Status);
        var existing = await FindMappingAsync("Estimate", est.Id, ct);
        if (existing is not null)
        {
            var row = await db.Estimates.FirstOrDefaultAsync(e => e.Id == existing.LocalId, ct);
            if (row is not null) ApplyEstimate(row, est, localClientId, status);
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var created = new Estimate();
        ApplyEstimate(created, est, localClientId, status);
        db.Estimates.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Estimate", est.Id, created.Id, ct);
    }

    private void ApplyEstimate(Estimate row, PaymoEstimate est, long? localClientId, EstimateStatus status)
    {
        row.Number = est.Number; row.ClientId = localClientId; row.Status = status;
        row.IssueDate = est.Date ?? clock.UtcNow; row.ExpiryDate = est.ExpiryDate;
        row.Currency = string.IsNullOrWhiteSpace(est.Currency) ? "USD" : est.Currency!;
        row.Subtotal = est.Subtotal; row.TaxAmount = est.TaxAmount; row.Total = est.Total;
        row.TaxRate = est.Subtotal > 0 ? Math.Round(est.TaxAmount / est.Subtotal * 100m, 2) : 0m;
    }

    private static InvoiceStatus MapInvoiceStatus(string? s)
    {
        var t = (s ?? "").ToLowerInvariant();
        if (t.Contains("paid")) return InvoiceStatus.Paid;
        if (t.Contains("overdue")) return InvoiceStatus.Overdue;
        if (t.Contains("void") || t.Contains("cancel")) return InvoiceStatus.Cancelled;
        if (t.Contains("sent") || t.Contains("view") || t.Contains("invoiced")) return InvoiceStatus.Sent;
        return InvoiceStatus.Draft;
    }

    private static EstimateStatus MapEstimateStatus(string? s)
    {
        var t = (s ?? "").ToLowerInvariant();
        if (t.Contains("accept") || t.Contains("approv")) return EstimateStatus.Accepted;
        if (t.Contains("reject") || t.Contains("declin")) return EstimateStatus.Rejected;
        if (t.Contains("expir")) return EstimateStatus.Expired;
        if (t.Contains("sent") || t.Contains("view")) return EstimateStatus.Sent;
        return EstimateStatus.Draft;
    }

    private async Task UpsertBookingAsync(PaymoBooking bk, long localUserId, long localProjectId, long? localTaskId, CancellationToken ct)
    {
        var start = bk.StartDate ?? clock.UtcNow;
        var end = bk.EndDate ?? start;
        var existing = await FindMappingAsync("Booking", bk.Id, ct);
        if (existing is not null)
        {
            var row = await db.Bookings.FirstOrDefaultAsync(x => x.Id == existing.LocalId, ct);
            if (row is not null)
            {
                row.UserId = localUserId; row.ProjectId = localProjectId; row.TaskId = localTaskId;
                row.StartDate = start; row.EndDate = end; row.HoursPerDay = bk.HoursPerDay; row.Description = bk.Description;
            }
            existing.LastSyncedUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var created = new Booking
        {
            UserId = localUserId, ProjectId = localProjectId, TaskId = localTaskId,
            StartDate = start, EndDate = end, HoursPerDay = bk.HoursPerDay, Description = bk.Description
        };
        db.Bookings.Add(created);
        await db.SaveChangesAsync(ct);
        await AddMappingAsync("Booking", bk.Id, created.Id, ct);
    }

    // Map a Paymo task to a WorkStatus using its workflow status (status_id) when known,
    // falling back to the boolean 'complete' flag. action = Paymo's backlog/complete marker.
    private static WorkStatus MapWorkStatus(PaymoTask t, IReadOnlyDictionary<long, PaymoWorkflowStatus> wfStatus)
    {
        if (t.StatusId is { } sid && wfStatus.TryGetValue(sid, out var ws))
        {
            var action = (ws.Action ?? "").ToLowerInvariant();
            var name = (ws.Name ?? "").ToLowerInvariant();
            if (action.Contains("complete") || name.Contains("complete") || name.Contains("done") || name.Contains("closed")) return WorkStatus.Done;
            if (name.Contains("block")) return WorkStatus.Blocked;
            if (name.Contains("review") || name.Contains("qa") || name.Contains("test") || name.Contains("approval")) return WorkStatus.InReview;
            if (name.Contains("progress") || name.Contains("doing") || name.Contains("active") || name.Contains("working")) return WorkStatus.InProgress;
            if (action.Contains("backlog") || name.Contains("backlog") || name.Contains("todo") || name.Contains("to do") || name.Contains("open") || name.Contains("new")) return WorkStatus.Todo;
        }
        return t.Complete ? WorkStatus.Done : WorkStatus.Todo;   // fallback
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
        var bookingIds = Ids("Booking");
        var fileIds = Ids("File");
        var entryIds = Ids("TimeEntry");
        var subtaskIds = Ids("Subtask");      // -> local ChecklistItems
        var commentIds = Ids("Comment");
        var discPostIds = Ids("DiscussionPost");
        var discIds = Ids("Discussion");
        var paymentIds = Ids("InvoicePayment");
        var expenseIds = Ids("Expense");
        var invoiceIds = Ids("Invoice");
        var estimateIds = Ids("Estimate");
        var taskIds = Ids("Task");
        var listIds = Ids("TaskList");
        var milestoneIds = Ids("Milestone");
        var projIds = Ids("Project");
        var contactIds = Ids("ClientContact");
        var clientIds = Ids("Client");
        var userIds = Ids("User");

        // Bookings first (Restrict FKs to user/project/task).
        if (bookingIds.Length > 0) await db.Bookings.IgnoreQueryFilters().Where(x => bookingIds.Contains(x.Id)).ExecuteDeleteAsync(ct);

        // Financials (payments → invoice line items → invoices; estimate line items → estimates; expenses).
        if (paymentIds.Length > 0) await db.InvoicePayments.IgnoreQueryFilters().Where(x => paymentIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (expenseIds.Length > 0) await db.Expenses.IgnoreQueryFilters().Where(x => expenseIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (invoiceIds.Length > 0)
        {
            await db.InvoiceLineItems.IgnoreQueryFilters().Where(x => invoiceIds.Contains(x.InvoiceId)).ExecuteDeleteAsync(ct);
            await db.InvoicePayments.IgnoreQueryFilters().Where(x => invoiceIds.Contains(x.InvoiceId)).ExecuteDeleteAsync(ct);
            await db.Invoices.IgnoreQueryFilters().Where(x => invoiceIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }
        if (estimateIds.Length > 0)
        {
            await db.EstimateLineItems.IgnoreQueryFilters().Where(x => estimateIds.Contains(x.EstimateId)).ExecuteDeleteAsync(ct);
            await db.Estimates.IgnoreQueryFilters().Where(x => estimateIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }

        if (fileIds.Length > 0) await db.Files.IgnoreQueryFilters().Where(x => fileIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (entryIds.Length > 0) await db.TimeEntries.IgnoreQueryFilters().Where(x => entryIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (subtaskIds.Length > 0) await db.ChecklistItems.IgnoreQueryFilters().Where(x => subtaskIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (commentIds.Length > 0) await db.Comments.IgnoreQueryFilters().Where(x => commentIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (discPostIds.Length > 0) await db.DiscussionPosts.IgnoreQueryFilters().Where(x => discPostIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (discIds.Length > 0) await db.DiscussionPosts.IgnoreQueryFilters().Where(x => discIds.Contains(x.DiscussionId)).ExecuteDeleteAsync(ct);
        if (discIds.Length > 0) await db.Discussions.IgnoreQueryFilters().Where(x => discIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (taskIds.Length > 0) await db.TaskAssignees.Where(x => taskIds.Contains(x.TaskId)).ExecuteDeleteAsync(ct);
        if (taskIds.Length > 0) await db.Tasks.IgnoreQueryFilters().Where(x => taskIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (listIds.Length > 0) await db.TaskLists.IgnoreQueryFilters().Where(x => listIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (milestoneIds.Length > 0) await db.Milestones.IgnoreQueryFilters().Where(x => milestoneIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (projIds.Length > 0) await db.Projects.IgnoreQueryFilters().Where(x => projIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (contactIds.Length > 0) await db.ClientContacts.IgnoreQueryFilters().Where(x => contactIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (clientIds.Length > 0) await db.Clients.IgnoreQueryFilters().Where(x => clientIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (userIds.Length > 0) await db.Users.IgnoreQueryFilters().Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync(ct);

        var tid = tenant.TenantId;
        await db.EntityMappings.IgnoreQueryFilters().Where(m => tenant.IsSuperAdmin || m.TenantId == tid).ExecuteDeleteAsync(ct);
        await db.MigrationErrors.IgnoreQueryFilters().Where(m => tenant.IsSuperAdmin || m.TenantId == tid).ExecuteDeleteAsync(ct);
        await db.MigrationProjectItems.IgnoreQueryFilters().Where(m => tenant.IsSuperAdmin || m.TenantId == tid).ExecuteDeleteAsync(ct);
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

    // Idempotent mapping insert (used for resume markers + thread links that may be re-touched).
    private async Task EnsureMappingAsync(string entityType, long paymoId, long localId, CancellationToken ct)
    {
        if (await FindMappingAsync(entityType, paymoId, ct) is not null) return;
        await AddMappingAsync(entityType, paymoId, localId, ct);
    }

    // Rebuild the in-memory cross-project maps from persisted EntityMappings so a resumed run can
    // skip completed work AND still anchor global passes (comments/files/bookings) correctly.
    private async Task RehydrateContextAsync(ImportContext ctx, CancellationToken ct)
    {
        var maps = await db.EntityMappings.AsNoTracking()
            .Where(m => m.EntityType == "Project" || m.EntityType == "Task" || m.EntityType == "Discussion"
                     || m.EntityType == "Comment" || m.EntityType == "TaskThread" || m.EntityType == "DiscThread")
            .Select(m => new { m.EntityType, m.PaymoId, m.LocalId })
            .ToListAsync(ct);
        foreach (var m in maps)
        {
            switch (m.EntityType)
            {
                case "Project": ctx.ProjectByPaymo[m.PaymoId] = m.LocalId; break;
                case "Task": ctx.TaskByPaymo[m.PaymoId] = m.LocalId; break;
                case "Discussion": ctx.DiscussionByPaymo[m.PaymoId] = m.LocalId; break;
                case "Comment": ctx.CommentByPaymo[m.PaymoId] = m.LocalId; break;
                case "TaskThread": ctx.ThreadToTask[m.PaymoId] = m.LocalId; break;
                case "DiscThread": ctx.ThreadToDiscussion[m.PaymoId] = m.LocalId; break;
            }
        }
    }

    private async Task<Dictionary<long, long>> LoadIdMapAsync(string entityType, CancellationToken ct)
        => await db.EntityMappings.AsNoTracking().Where(m => m.EntityType == entityType)
            .ToDictionaryAsync(m => m.PaymoId, m => m.LocalId, ct);

    // Upsert the per-project catalog from Paymo's project list: add new projects as Pending, refresh names.
    // Never downgrades an Imported/Failed status (only the migration loop changes those).
    private async Task SyncCatalogAsync(IReadOnlyList<PaymoProject> projects, CancellationToken ct)
    {
        if (projects.Count == 0) return;
        var existing = await db.MigrationProjectItems.ToDictionaryAsync(i => i.PaymoProjectId, ct);
        // Carry over projects already fully imported by a prior (pre-staging) run, so the new batched
        // flow marks them done instead of re-importing (and re-hitting Paymo's rate limit).
        var doneMap = await db.EntityMappings.AsNoTracking()
            .Where(m => m.EntityType == "ProjectDone")
            .ToDictionaryAsync(m => m.PaymoId, m => m.LocalId, ct);
        var seq = 0;
        foreach (var p in projects)
        {
            seq++;
            if (existing.TryGetValue(p.Id, out var row))
            {
                row.Name = p.Name;
                row.Seq = seq;
            }
            else
            {
                var carriedOver = doneMap.TryGetValue(p.Id, out var localId);
                db.MigrationProjectItems.Add(new MigrationProjectItem
                {
                    PaymoProjectId = p.Id, Name = p.Name, Seq = seq,
                    Status = carriedOver ? MigrationProjectStatus.Imported : MigrationProjectStatus.Pending,
                    LocalProjectId = carriedOver ? localId : null,
                    ImportedAtUtc = carriedOver ? clock.UtcNow : null
                });
            }
        }
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

    // Fetch a Paymo list resiliently: a failure (e.g. 400 on an unsupported filter) is recorded as a
    // non-fatal migration error and yields an empty list, so one bad resource never aborts the whole run.
    private async Task<IReadOnlyList<T>> SafeFetchAsync<T>(MigrationJob job, string entityType, long contextId,
        Func<Task<IReadOnlyList<T>>> fetch, CancellationToken ct)
    {
        try { return await fetch(); }
        catch (Exception ex) { await RecordError(job, entityType, contextId, ex, $"fetch {entityType} (ctx {contextId})", ct); return []; }
    }

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
