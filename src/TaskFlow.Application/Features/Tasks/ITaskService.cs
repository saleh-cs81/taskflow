using TaskFlow.Application.Common.Models;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Tasks;

public interface ITaskService
{
    Task<PagedResult<TaskDto>> ListAsync(long projectId, PageQuery page, WorkStatus? status, long? assigneeId, CancellationToken ct = default);
    // Open tasks assigned to the current user across all projects (for the Home hub).
    Task<IReadOnlyList<AssignedTaskDto>> ListMineAsync(bool includeDone = false, CancellationToken ct = default);
    Task<TaskDto> GetAsync(long id, CancellationToken ct = default);
    Task<TaskDto> CreateAsync(CreateTaskRequest request, CancellationToken ct = default);
    Task<TaskDto> UpdateAsync(long id, UpdateTaskRequest request, CancellationToken ct = default);
    Task<TaskDto> MoveAsync(long id, MoveTaskRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    // Task lists (Kanban columns)
    Task<TaskListDto> CreateListAsync(CreateTaskListRequest request, CancellationToken ct = default);
    Task DeleteListAsync(long listId, CancellationToken ct = default);

    // Checklist
    Task<IReadOnlyList<ChecklistItemDto>> GetChecklistAsync(long taskId, CancellationToken ct = default);
    Task<ChecklistItemDto> AddChecklistItemAsync(long taskId, ChecklistItemRequest request, CancellationToken ct = default);
    Task<ChecklistItemDto> ToggleChecklistItemAsync(long taskId, long itemId, CancellationToken ct = default);
    Task DeleteChecklistItemAsync(long taskId, long itemId, CancellationToken ct = default);

    // Watchers
    Task AddWatcherAsync(long taskId, long userId, CancellationToken ct = default);
    Task RemoveWatcherAsync(long taskId, long userId, CancellationToken ct = default);

    // Multiple assignees (many-to-many; TaskItem.AssigneeId stays as the primary assignee).
    Task AddAssigneeAsync(long taskId, long userId, CancellationToken ct = default);
    Task RemoveAssigneeAsync(long taskId, long userId, CancellationToken ct = default);

    // Dependencies
    Task<DependencyDto> AddDependencyAsync(long taskId, AddDependencyRequest request, CancellationToken ct = default);
    Task RemoveDependencyAsync(long taskId, long dependencyId, CancellationToken ct = default);

    // Tags
    Task<IReadOnlyList<TagDto>> ListTagsAsync(CancellationToken ct = default);
    Task<TagDto> CreateTagAsync(TagRequest request, CancellationToken ct = default);
    Task AddTagToTaskAsync(long taskId, long tagId, CancellationToken ct = default);
    Task RemoveTagFromTaskAsync(long taskId, long tagId, CancellationToken ct = default);

    // Comments
    Task<IReadOnlyList<CommentDto>> GetCommentsAsync(long taskId, CancellationToken ct = default);
    Task<CommentDto> AddCommentAsync(long taskId, AddCommentRequest request, CancellationToken ct = default);
}
