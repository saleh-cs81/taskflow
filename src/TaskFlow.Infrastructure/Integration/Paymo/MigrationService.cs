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

            // 2) Project statuses (reference vocabulary: paymo status id -> name).
            var statusMap = new Dictionary<long, string>();
            try { foreach (var s in await paymo.GetProjectStatusesAsync(apiKey, ct)) statusMap[s.Id] = s.Name; }
            catch (Exception ex) { await RecordError(job, "ProjectStatus", 0, ex, "statuses", ct); }

            // 3) Clients, then their contacts.
            var clientMap = new Dictionary<long, long>();
            foreach (var c in await paymo.GetClientsAsync(apiKey, since, ct))
            {
                try { clientMap[c.Id] = await UpsertClientAsync(c, ct); job.ProcessedRecords++; }
                catch (Exception ex) { await RecordError(job, "Client", c.Id, ex, c, ct); }
            }
            try
            {
                foreach (var cc in await paymo.GetClientContactsAsync(apiKey, ct))
                {
                    if (!clientMap.TryGetValue(cc.ClientId, out var localClient)) continue;
                    try { await UpsertClientContactAsync(cc, localClient, ct); job.ProcessedRecords++; }
                    catch (Exception ex) { await RecordError(job, "ClientContact", cc.Id, ex, cc, ct); }
                }
            }
            catch (Exception ex) { await RecordError(job, "ClientContact", 0, ex, "contacts", ct); }

            job.Message = $"Imported {userMap.Count} users, {clientMap.Count} clients…";
            await db.SaveChangesAsync(ct);

            // 4) Projects (outer loop drives the progress bar). Accumulate cross-project
            // maps so comments/files (fetched globally) can be anchored afterwards.
            var ctx = new ImportContext(userMap);
            var projects = await paymo.GetProjectsAsync(apiKey, since, ct);
            job.ProjectsTotal = projects.Count;
            await db.SaveChangesAsync(ct);

            foreach (var p in projects)
            {
                long localProjectId;
                try { localProjectId = await UpsertProjectAsync(p, clientMap, statusMap, ct); job.ProcessedRecords++; }
                catch (Exception ex) { await RecordError(job, "Project", p.Id, ex, p, ct); job.ProjectsDone++; await db.SaveChangesAsync(ct); continue; }

                ctx.ProjectByPaymo[p.Id] = localProjectId;
                await ImportProjectChildren(job, apiKey, p.Id, localProjectId, since, ctx, ct);

                job.ProjectsDone++;
                job.Message = $"Imported {job.ProjectsDone}/{job.ProjectsTotal} projects · {job.ProcessedRecords} records";
                await db.SaveChangesAsync(ct);   // persist progress per project for live polling
            }

            // 5) Comments (global; anchored to imported task/discussion threads).
            await ImportCommentsAsync(job, apiKey, ctx, ct);

            // 6) Files (global; downloaded + stored, attached to task/comment/project).
            await ImportFilesAsync(job, apiKey, ctx, ct);
            await db.SaveChangesAsync(ct);

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
    }

    private async Task ImportProjectChildren(MigrationJob job, string apiKey, long paymoProjectId, long localProjectId,
        DateTime? since, ImportContext ctx, CancellationToken ct)
    {
        var userMap = ctx.UserMap;

        // Milestones (project-level), so tasks can be linked through their task list.
        var milestoneMap = new Dictionary<long, long>();
        foreach (var m in await paymo.GetMilestonesAsync(apiKey, paymoProjectId, ct))
        {
            try { milestoneMap[m.Id] = await UpsertMilestoneAsync(m, localProjectId, ct); job.ProcessedRecords++; }
            catch (Exception ex) { await RecordError(job, "Milestone", m.Id, ex, m, ct); }
        }

        // Task lists (capture each list's milestone link for task-level wiring).
        var listMap = new Dictionary<long, long>();
        var listMilestone = new Dictionary<long, long>(); // paymo list id -> paymo milestone id
        foreach (var l in await paymo.GetTaskListsAsync(apiKey, paymoProjectId, ct))
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
        foreach (var t in await paymo.GetTasksAsync(apiKey, paymoProjectId, since, ct))
        {
            try
            {
                long? localList = t.TaskListId is { } lid && listMap.TryGetValue(lid, out var ll) ? ll : null;
                long? localMilestone = t.TaskListId is { } lid2 && listMilestone.TryGetValue(lid2, out var pm)
                    && milestoneMap.TryGetValue(pm, out var lm) ? lm : null;
                var assignees = t.AssigneeUserIds.Where(userMap.ContainsKey).Select(a => userMap[a]).ToList();
                var localTaskId = await UpsertTaskAsync(t, localProjectId, localList, localMilestone, assignees, ct);
                ctx.TaskByPaymo[t.Id] = localTaskId;
                if (t.ThreadId is { } th) ctx.ThreadToTask[th] = localTaskId;
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "Task", t.Id, ex, t, ct); }
        }

        // Subtasks -> local checklist items under the parent task.
        foreach (var s in await paymo.GetSubtasksAsync(apiKey, paymoProjectId, ct))
        {
            if (!ctx.TaskByPaymo.TryGetValue(s.TaskId, out var localTask)) continue;
            try { await UpsertSubtaskAsync(s, localTask, ct); job.ProcessedRecords++; }
            catch (Exception ex) { await RecordError(job, "Subtask", s.Id, ex, s, ct); }
        }

        // Discussions (+ description as first post).
        foreach (var d in await paymo.GetDiscussionsAsync(apiKey, paymoProjectId, ct))
        {
            try
            {
                long dAuthor = d.UserId is { } du && userMap.TryGetValue(du, out var dl) ? dl : _runUserId;
                var localDisc = await UpsertDiscussionAsync(d, localProjectId, dAuthor, ct);
                ctx.DiscussionByPaymo[d.Id] = localDisc;
                if (d.ThreadId is { } th) ctx.ThreadToDiscussion[th] = localDisc;
                job.ProcessedRecords++;
            }
            catch (Exception ex) { await RecordError(job, "Discussion", d.Id, ex, d, ct); }
        }

        // Time entries.
        foreach (var e in await paymo.GetTimeEntriesAsync(apiKey, paymoProjectId, since, ct))
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
    }

    // Comments are polymorphic: anchor by thread_id to a task (-> Comment) or a discussion (-> DiscussionPost).
    private async Task ImportCommentsAsync(MigrationJob job, string apiKey, ImportContext ctx, CancellationToken ct)
    {
        IReadOnlyList<PaymoComment> comments;
        try { comments = await paymo.GetCommentsAsync(apiKey, ct); }
        catch (Exception ex) { await RecordError(job, "Comment", 0, ex, "comments", ct); return; }

        foreach (var c in comments)
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
                // else: comment on an un-imported thread (file/project) — skipped.
            }
            catch (Exception ex) { await RecordError(job, "Comment", c.Id, ex, c, ct); }
        }
        job.Message = $"Imported comments… {job.ProcessedRecords} records";
        await db.SaveChangesAsync(ct);
    }

    private async Task ImportFilesAsync(MigrationJob job, string apiKey, ImportContext ctx, CancellationToken ct)
    {
        IReadOnlyList<PaymoFile> files;
        try { files = await paymo.GetFilesAsync(apiKey, ct); }
        catch (Exception ex) { await RecordError(job, "File", 0, ex, "files", ct); return; }

        foreach (var f in files)
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
        job.Message = $"Imported files… {job.ProcessedRecords} records";
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

        var download = await paymo.GetFileBytesAsync(apiKey, f.Id, ct)
            ?? throw new InvalidOperationException($"File {f.Id} could not be downloaded.");
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
        IReadOnlyList<long> assigneeIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) throw new InvalidOperationException("Task title is required.");
        var status = t.Complete ? WorkStatus.Done : WorkStatus.Todo;
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
        var fileIds = Ids("File");
        var entryIds = Ids("TimeEntry");
        var subtaskIds = Ids("Subtask");      // -> local ChecklistItems
        var commentIds = Ids("Comment");
        var discPostIds = Ids("DiscussionPost");
        var discIds = Ids("Discussion");
        var taskIds = Ids("Task");
        var listIds = Ids("TaskList");
        var milestoneIds = Ids("Milestone");
        var projIds = Ids("Project");
        var contactIds = Ids("ClientContact");
        var clientIds = Ids("Client");
        var userIds = Ids("User");

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
