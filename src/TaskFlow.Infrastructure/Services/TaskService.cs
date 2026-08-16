using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Notifications;
using TaskFlow.Application.Features.Tasks;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class TaskService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTime clock,
    INotificationService notifications,
    IActivityService activity,
    IRealtimeNotifier realtime) : ITaskService
{
    public async Task<PagedResult<TaskDto>> ListAsync(long projectId, PageQuery page, WorkStatus? status, long? assigneeId, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId);
        if (status is not null) query = query.Where(t => t.Status == status);
        if (assigneeId is not null) query = query.Where(t => t.AssigneeId == assigneeId);

        var total = await query.CountAsync(ct);
        var ids = await query.OrderBy(t => t.Position).ThenByDescending(t => t.CreatedAtUtc)
            .Skip(page.Skip).Take(page.NormalizedSize).Select(t => t.Id).ToListAsync(ct);

        var items = await BuildTaskDtos(ids, ct);
        return new PagedResult<TaskDto>(items, page.NormalizedPage, page.NormalizedSize, total);
    }

    public async Task<IReadOnlyList<AssignedTaskDto>> ListMineAsync(bool includeDone = false, CancellationToken ct = default)
    {
        var me = currentUser.UserId;
        if (me is null) return [];

        // Assigned via the primary AssigneeId OR the many-to-many TaskAssignee set.
        var query = db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == me || db.TaskAssignees.Any(a => a.TaskId == t.Id && a.UserId == me));
        if (!includeDone) query = query.Where(t => t.Status != WorkStatus.Done);

        return await query
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.CreatedAtUtc)
            .Select(t => new AssignedTaskDto(
                t.Id, t.ProjectId, t.Project.Name, t.Title, t.Status, t.Priority, t.DueDate, t.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<TaskDto> GetAsync(long id, CancellationToken ct = default)
    {
        var dtos = await BuildTaskDtos([id], ct);
        return dtos.FirstOrDefault() ?? throw new NotFoundAppException("error.not_found");
    }

    public async Task<TaskDto> CreateAsync(CreateTaskRequest r, CancellationToken ct = default)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct))
            throw new NotFoundAppException("error.not_found");

        var nextPosition = await NextPositionAsync(r.TaskListId, ct);

        var task = new TaskItem
        {
            ProjectId = r.ProjectId,
            TaskListId = r.TaskListId,
            ParentTaskId = r.ParentTaskId,
            MilestoneId = r.MilestoneId,
            Title = r.Title.Trim(),
            Description = r.Description,
            Status = WorkStatus.Todo,
            Priority = r.Priority,
            AssigneeId = r.AssigneeId,
            ReporterId = currentUser.UserId,
            StartDate = r.StartDate,
            DueDate = r.DueDate,
            EstimateHours = r.EstimateHours,
            IsBillable = r.IsBillable,
            Position = nextPosition
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        await activity.LogAsync("task.created", "Task", task.Id, ct);
        await NotifyAssignee(task, ct);
        await realtime.NotifyProjectAsync(task.ProjectId, "task.created", new { task.Id, task.ProjectId, task.TaskListId }, ct);
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskDto> UpdateAsync(long id, UpdateTaskRequest r, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");

        var wasDone = task.Status == WorkStatus.Done;
        var previousAssignee = task.AssigneeId;
        task.Title = r.Title.Trim();
        task.Description = r.Description;
        task.Status = r.Status;
        task.Priority = r.Priority;
        task.AssigneeId = r.AssigneeId;
        task.MilestoneId = r.MilestoneId;
        task.StartDate = r.StartDate;
        task.DueDate = r.DueDate;
        task.EstimateHours = r.EstimateHours;
        task.IsBillable = r.IsBillable;

        if (r.Status == WorkStatus.Done && !wasDone) task.CompletedAtUtc = clock.UtcNow;
        else if (r.Status != WorkStatus.Done) task.CompletedAtUtc = null;

        await db.SaveChangesAsync(ct);

        if (r.Status == WorkStatus.Done && !wasDone)
            await NotifyTaskAsync(task, "completed", NotificationType.TaskCompleted, null, null, ct);
        else if (task.AssigneeId is not null && task.AssigneeId != previousAssignee)
            await NotifyTaskAsync(task, "assigned", NotificationType.TaskAssigned, null, null, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDto> MoveAsync(long id, MoveTaskRequest r, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");

        // Move to another project: reset list/milestone (they belong to the old project).
        if (r.ProjectId is not null && r.ProjectId != task.ProjectId)
        {
            var projOk = await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct);
            if (!projOk) throw new ValidationAppException("error.validation");
            task.ProjectId = r.ProjectId.Value;
            task.TaskListId = null;
            task.MilestoneId = null;
        }

        if (r.TaskListId is not null && r.TaskListId != task.TaskListId)
        {
            var listOk = await db.TaskLists.AnyAsync(l => l.Id == r.TaskListId && l.ProjectId == task.ProjectId, ct);
            if (!listOk) throw new ValidationAppException("error.validation");
            task.TaskListId = r.TaskListId;
        }

        task.Position = r.Position;
        if (r.Status is not null)
        {
            task.Status = r.Status.Value;
            task.CompletedAtUtc = r.Status == WorkStatus.Done ? clock.UtcNow : null;
        }
        await db.SaveChangesAsync(ct);

        await realtime.NotifyProjectAsync(task.ProjectId, "task.moved",
            new { task.Id, task.ProjectId, task.TaskListId, task.Position, task.Status }, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TaskListDto> CreateListAsync(CreateTaskListRequest r, CancellationToken ct = default)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct))
            throw new NotFoundAppException("error.not_found");
        var maxPos = await db.TaskLists.Where(l => l.ProjectId == r.ProjectId).MaxAsync(l => (double?)l.Position, ct) ?? 0;
        var list = new TaskList { ProjectId = r.ProjectId, Name = r.Name.Trim(), Position = maxPos + 1000 };
        db.TaskLists.Add(list);
        await db.SaveChangesAsync(ct);
        return new TaskListDto(list.Id, list.ProjectId, list.Name, list.Position);
    }

    public async Task DeleteListAsync(long listId, CancellationToken ct = default)
    {
        var list = await db.TaskLists.FirstOrDefaultAsync(l => l.Id == listId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.TaskLists.Remove(list);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChecklistItemDto>> GetChecklistAsync(long taskId, CancellationToken ct = default)
        => await db.ChecklistItems.AsNoTracking().Where(c => c.TaskId == taskId)
            .OrderBy(c => c.Position)
            .Select(c => new ChecklistItemDto(c.Id, c.TaskId, c.Text, c.IsDone, c.Position))
            .ToListAsync(ct);

    public async Task<ChecklistItemDto> AddChecklistItemAsync(long taskId, ChecklistItemRequest r, CancellationToken ct = default)
    {
        await EnsureTaskExists(taskId, ct);
        var maxPos = await db.ChecklistItems.Where(c => c.TaskId == taskId).MaxAsync(c => (double?)c.Position, ct) ?? 0;
        var item = new ChecklistItem { TaskId = taskId, Text = r.Text.Trim(), Position = maxPos + 1000 };
        db.ChecklistItems.Add(item);
        await db.SaveChangesAsync(ct);
        return new ChecklistItemDto(item.Id, taskId, item.Text, item.IsDone, item.Position);
    }

    public async Task<ChecklistItemDto> ToggleChecklistItemAsync(long taskId, long itemId, CancellationToken ct = default)
    {
        var item = await db.ChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId && c.TaskId == taskId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        item.IsDone = !item.IsDone;
        await db.SaveChangesAsync(ct);
        return new ChecklistItemDto(item.Id, taskId, item.Text, item.IsDone, item.Position);
    }

    public async Task DeleteChecklistItemAsync(long taskId, long itemId, CancellationToken ct = default)
    {
        var item = await db.ChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId && c.TaskId == taskId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.ChecklistItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddWatcherAsync(long taskId, long userId, CancellationToken ct = default)
    {
        await EnsureTaskExists(taskId, ct);
        if (await db.TaskWatchers.AnyAsync(w => w.TaskId == taskId && w.UserId == userId, ct)) return;
        db.TaskWatchers.Add(new TaskWatcher { TaskId = taskId, UserId = userId });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveWatcherAsync(long taskId, long userId, CancellationToken ct = default)
    {
        var w = await db.TaskWatchers.FirstOrDefaultAsync(x => x.TaskId == taskId && x.UserId == userId, ct);
        if (w is null) return;
        db.TaskWatchers.Remove(w);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddAssigneeAsync(long taskId, long userId, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        if (await db.TaskAssignees.AnyAsync(a => a.TaskId == taskId && a.UserId == userId, ct)) return;
        db.TaskAssignees.Add(new TaskAssignee { TaskId = taskId, UserId = userId });
        if (task.AssigneeId is null) task.AssigneeId = userId; // keep the primary assignee in sync for the single-assignee UI
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAssigneeAsync(long taskId, long userId, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        var a = await db.TaskAssignees.FirstOrDefaultAsync(x => x.TaskId == taskId && x.UserId == userId, ct);
        var wasPrimary = task.AssigneeId == userId;
        if (a is null && !wasPrimary) return; // user isn't assigned to this task at all
        if (a is not null) db.TaskAssignees.Remove(a);
        // Reassign the primary to another assignee (or clear it). Also covers legacy tasks whose
        // primary assignee was set at creation without a TaskAssignees join row.
        if (wasPrimary)
            task.AssigneeId = await db.TaskAssignees
                .Where(x => x.TaskId == taskId && x.UserId != userId)
                .Select(x => (long?)x.UserId).FirstOrDefaultAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DependencyDto> AddDependencyAsync(long taskId, AddDependencyRequest r, CancellationToken ct = default)
    {
        await EnsureTaskExists(taskId, ct);
        if (r.PredecessorTaskId == taskId) throw new ValidationAppException("error.validation");
        if (!await db.Tasks.AnyAsync(t => t.Id == r.PredecessorTaskId, ct))
            throw new NotFoundAppException("error.not_found");
        if (await db.TaskDependencies.AnyAsync(d => d.PredecessorTaskId == r.PredecessorTaskId && d.SuccessorTaskId == taskId, ct))
            throw new ConflictAppException("error.conflict");

        var dep = new TaskDependency { PredecessorTaskId = r.PredecessorTaskId, SuccessorTaskId = taskId, Type = r.Type };
        db.TaskDependencies.Add(dep);
        await db.SaveChangesAsync(ct);
        return new DependencyDto(dep.Id, dep.PredecessorTaskId, dep.SuccessorTaskId, dep.Type);
    }

    public async Task RemoveDependencyAsync(long taskId, long dependencyId, CancellationToken ct = default)
    {
        var dep = await db.TaskDependencies.FirstOrDefaultAsync(d => d.Id == dependencyId && d.SuccessorTaskId == taskId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.TaskDependencies.Remove(dep);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TagDto>> ListTagsAsync(CancellationToken ct = default)
        => await db.Tags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, t.Color)).ToListAsync(ct);

    public async Task<TagDto> CreateTagAsync(TagRequest r, CancellationToken ct = default)
    {
        var name = r.Name.Trim();
        if (await db.Tags.AnyAsync(t => t.Name == name, ct))
            throw new ConflictAppException("error.conflict");
        var tag = new Tag { Name = name, Color = r.Color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return new TagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task AddTagToTaskAsync(long taskId, long tagId, CancellationToken ct = default)
    {
        await EnsureTaskExists(taskId, ct);
        if (!await db.Tags.AnyAsync(t => t.Id == tagId, ct)) throw new NotFoundAppException("error.not_found");
        if (await db.TaskTags.AnyAsync(tt => tt.TaskId == taskId && tt.TagId == tagId, ct)) return;
        db.TaskTags.Add(new TaskTag { TaskId = taskId, TagId = tagId });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTagFromTaskAsync(long taskId, long tagId, CancellationToken ct = default)
    {
        var tt = await db.TaskTags.FirstOrDefaultAsync(x => x.TaskId == taskId && x.TagId == tagId, ct);
        if (tt is null) return;
        db.TaskTags.Remove(tt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CommentDto>> GetCommentsAsync(long taskId, CancellationToken ct = default)
        => await db.Comments.AsNoTracking()
            .Where(c => c.TargetType == CommentTargetType.Task && c.TargetId == taskId)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new CommentDto(c.Id, c.AuthorId, c.Body, c.ParentCommentId, c.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<CommentDto> AddCommentAsync(long taskId, AddCommentRequest r, CancellationToken ct = default)
    {
        await EnsureTaskExists(taskId, ct);
        var comment = new Comment
        {
            TargetType = CommentTargetType.Task,
            TargetId = taskId,
            AuthorId = currentUser.UserId ?? 0,
            Body = r.Body.Trim(),
            ParentCommentId = r.ParentCommentId
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        var authorId = currentUser.UserId;
        var link = $"/tasks/{taskId}";

        // @mentions → Mention rows + notifications.
        var mentioned = (r.MentionedUserIds ?? []).Distinct().Where(uid => uid != authorId).ToList();
        foreach (var uid in mentioned)
        {
            db.Mentions.Add(new Mention { CommentId = comment.Id, MentionedUserId = uid });
        }
        if (mentioned.Count > 0) await db.SaveChangesAsync(ct);
        foreach (var uid in mentioned)
        {
            await notifications.CreateAsync(new CreateNotification(
                uid, NotificationType.Mention, "You were mentioned in a comment", comment.Body, link), ct);
        }

        // Notify assignees + company admins that a comment was posted (mentioned users already notified above).
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is not null)
            await NotifyTaskAsync(task, "commented on", NotificationType.TaskCommented, comment.Body, mentioned, ct);

        await activity.LogAsync("task.commented", "Task", taskId, ct);
        return new CommentDto(comment.Id, comment.AuthorId, comment.Body, comment.ParentCommentId, comment.CreatedAtUtc);
    }

    private async Task NotifyAssignee(TaskItem task, CancellationToken ct)
        => await NotifyTaskAsync(task, "created", NotificationType.TaskAssigned, null, null, ct);

    // Rich-text task notification ("Lama completed the SMTP task in cpt-jo.com") sent to every
    // assignee of the task plus every company admin (minus the actor and any excluded users).
    private async Task NotifyTaskAsync(TaskItem task, string verb, NotificationType type, string? body,
        IEnumerable<long>? exclude, CancellationToken ct)
    {
        var actor = currentUser.UserId;
        var actorName = actor is { } aid
            ? await db.Users.AsNoTracking().Where(u => u.Id == aid).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Someone"
            : "Someone";
        var projectName = await db.Projects.AsNoTracking().Where(p => p.Id == task.ProjectId)
            .Select(p => p.Name).FirstOrDefaultAsync(ct);
        var title = string.IsNullOrEmpty(projectName)
            ? $"{actorName} {verb} the {task.Title} task"
            : $"{actorName} {verb} the {task.Title} task in {projectName}";
        var link = $"/board.html?projectId={task.ProjectId}&openTask={task.Id}";

        var recipients = new HashSet<long>();
        if (task.AssigneeId is { } primary) recipients.Add(primary);
        foreach (var uid in await db.TaskAssignees.AsNoTracking().Where(a => a.TaskId == task.Id).Select(a => a.UserId).ToListAsync(ct))
            recipients.Add(uid);
        // Company admins receive every task notification.
        var adminIds = await (from ur in db.UserRoles
                              join ro in db.Roles on ur.RoleId equals ro.Id
                              where ro.Name == "CompanyAdmin" || ro.Name == "SuperAdmin"
                              select ur.UserId).Distinct().ToListAsync(ct);
        foreach (var uid in adminIds) recipients.Add(uid);

        if (actor is { } me) recipients.Remove(me);
        if (exclude is not null) foreach (var e in exclude) recipients.Remove(e);

        foreach (var uid in recipients)
            await notifications.CreateAsync(new CreateNotification(uid, type, title, body, link), ct);
    }

    private async Task<double> NextPositionAsync(long? taskListId, CancellationToken ct)
    {
        var max = await db.Tasks.Where(t => t.TaskListId == taskListId)
            .MaxAsync(t => (double?)t.Position, ct) ?? 0;
        return max + 1000;
    }

    private async Task EnsureTaskExists(long taskId, CancellationToken ct)
    {
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId, ct))
            throw new NotFoundAppException("error.not_found");
    }

    // Builds full task DTOs (with tag names + checklist/subtask counts) for a set of ids.
    private async Task<List<TaskDto>> BuildTaskDtos(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        var tasks = await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);

        var tags = await db.TaskTags.AsNoTracking().Where(tt => ids.Contains(tt.TaskId))
            .Select(tt => new { tt.TaskId, tt.Tag.Name }).ToListAsync(ct);
        var checklist = await db.ChecklistItems.AsNoTracking().Where(c => ids.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) })
            .ToListAsync(ct);
        var subtaskCounts = await db.Tasks.AsNoTracking()
            .Where(t => t.ParentTaskId != null && ids.Contains(t.ParentTaskId!.Value))
            .GroupBy(t => t.ParentTaskId!.Value)
            .Select(g => new { TaskId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var assignees = await db.TaskAssignees.AsNoTracking().Where(a => ids.Contains(a.TaskId))
            .Select(a => new { a.TaskId, a.UserId }).ToListAsync(ct);

        var order = ids.ToList();
        return tasks.OrderBy(t => order.IndexOf(t.Id)).Select(t =>
        {
            var taskTags = tags.Where(x => x.TaskId == t.Id).Select(x => x.Name).ToList();
            var cl = checklist.FirstOrDefault(x => x.TaskId == t.Id);
            var sc = subtaskCounts.FirstOrDefault(x => x.TaskId == t.Id);
            var asg = assignees.Where(x => x.TaskId == t.Id).Select(x => x.UserId).ToList();
            return new TaskDto(
                t.Id, t.ProjectId, t.TaskListId, t.ParentTaskId, t.MilestoneId, t.Title, t.Description,
                t.Status, t.Priority, t.AssigneeId, t.ReporterId, t.StartDate, t.DueDate, t.EstimateHours,
                t.CompletedAtUtc, t.Position, t.IsBillable, taskTags,
                cl?.Total ?? 0, cl?.Done ?? 0, sc?.Count ?? 0, t.CreatedAtUtc, t.UpdatedAtUtc, asg);
        }).ToList();
    }
}
