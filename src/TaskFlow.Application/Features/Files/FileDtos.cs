using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Files;

public record FileDto(
    long Id,
    AttachmentTargetType TargetType,
    long TargetId,
    string FileName,
    string ContentType,
    long SizeBytes,
    int Version,
    long? CreatedById,
    DateTime CreatedAtUtc);

public record FileDownload(Stream Content, string FileName, string ContentType);

// A file as listed for a whole project, carrying the task it belongs to (if any) so the
// UI can group files under their task. TaskId/TaskTitle are null for project-level files.
public record ProjectFileDto(
    long Id,
    AttachmentTargetType TargetType,
    long TargetId,
    string FileName,
    string ContentType,
    long SizeBytes,
    long? TaskId,
    string? TaskTitle,
    DateTime CreatedAtUtc);

public interface IFileService
{
    Task<FileDto> UploadAsync(AttachmentTargetType targetType, long targetId, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<IReadOnlyList<FileDto>> ListAsync(AttachmentTargetType targetType, long targetId, CancellationToken ct = default);
    // All files anywhere in a project: project-level + on its tasks + on its task comments — each tagged with its task.
    Task<IReadOnlyList<ProjectFileDto>> ListForProjectAsync(long projectId, CancellationToken ct = default);
    Task<FileDownload> DownloadAsync(long id, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
