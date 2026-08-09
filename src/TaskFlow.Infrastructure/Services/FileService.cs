using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Files;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class FileService(IAppDbContext db, IFileStorage storage, ITenantContext tenant) : IFileService
{
    private const long MaxBytes = 50 * 1024 * 1024; // 50 MB

    public async Task<FileDto> UploadAsync(AttachmentTargetType targetType, long targetId, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new UnauthorizedAppException("error.unauthorized");
        await EnsureTargetExists(targetType, targetId, ct);

        var stored = await storage.SaveAsync(content, fileName, tenantId, ct);
        if (stored.SizeBytes > MaxBytes)
        {
            await storage.DeleteAsync(stored.StorageKey, ct);
            throw new ValidationAppException("error.validation");
        }

        var existingVersions = await db.Files
            .Where(f => f.TargetType == targetType && f.TargetId == targetId && f.FileName == fileName)
            .CountAsync(ct);

        var file = new FileObject
        {
            TargetType = targetType,
            TargetId = targetId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            StorageKey = stored.StorageKey,
            Version = existingVersions + 1
        };
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);
        return ToDto(file);
    }

    public async Task<IReadOnlyList<FileDto>> ListAsync(AttachmentTargetType targetType, long targetId, CancellationToken ct = default)
        => await db.Files.AsNoTracking()
            .Where(f => f.TargetType == targetType && f.TargetId == targetId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new FileDto(f.Id, f.TargetType, f.TargetId, f.FileName, f.ContentType, f.SizeBytes, f.Version, f.CreatedById, f.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProjectFileDto>> ListForProjectAsync(long projectId, CancellationToken ct = default)
    {
        // Task title lookup for the whole project, so every file can be tagged with its task.
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId)
            .Select(t => new { t.Id, t.Title }).ToListAsync(ct);
        var taskTitle = tasks.ToDictionary(t => t.Id, t => t.Title);
        var taskIds = taskTitle.Keys.ToList();

        // A file on a task comment still belongs to that comment's task.
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.TargetType == CommentTargetType.Task && taskIds.Contains(c.TargetId))
            .Select(c => new { c.Id, c.TargetId }).ToListAsync(ct);
        var commentToTask = comments.ToDictionary(c => c.Id, c => c.TargetId);
        var commentIds = commentToTask.Keys.ToList();

        var files = await db.Files.AsNoTracking()
            .Where(f => (f.TargetType == AttachmentTargetType.Project && f.TargetId == projectId)
                     || (f.TargetType == AttachmentTargetType.Task && taskIds.Contains(f.TargetId))
                     || (f.TargetType == AttachmentTargetType.Comment && commentIds.Contains(f.TargetId)))
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new { f.Id, f.TargetType, f.TargetId, f.FileName, f.ContentType, f.SizeBytes, f.CreatedAtUtc })
            .ToListAsync(ct);

        return files.Select(f =>
        {
            long? taskId = f.TargetType == AttachmentTargetType.Task ? f.TargetId
                         : f.TargetType == AttachmentTargetType.Comment && commentToTask.TryGetValue(f.TargetId, out var tk) ? tk
                         : (long?)null;
            var title = taskId is { } id && taskTitle.TryGetValue(id, out var t) ? t : null;
            return new ProjectFileDto(f.Id, f.TargetType, f.TargetId, f.FileName, f.ContentType, f.SizeBytes, taskId, title, f.CreatedAtUtc);
        }).ToList();
    }

    public async Task<FileDownload> DownloadAsync(long id, CancellationToken ct = default)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        var stream = await storage.OpenReadAsync(file.StorageKey, ct);
        return new FileDownload(stream, file.FileName, file.ContentType);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.Files.Remove(file); // soft delete; physical file retained for recovery
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureTargetExists(AttachmentTargetType type, long id, CancellationToken ct)
    {
        var exists = type switch
        {
            AttachmentTargetType.Task => await db.Tasks.AnyAsync(t => t.Id == id, ct),
            AttachmentTargetType.Project => await db.Projects.AnyAsync(p => p.Id == id, ct),
            AttachmentTargetType.Comment => await db.Comments.AnyAsync(c => c.Id == id, ct),
            _ => false
        };
        if (!exists) throw new NotFoundAppException("error.not_found");
    }

    private static FileDto ToDto(FileObject f) => new(
        f.Id, f.TargetType, f.TargetId, f.FileName, f.ContentType, f.SizeBytes, f.Version, f.CreatedById, f.CreatedAtUtc);
}
