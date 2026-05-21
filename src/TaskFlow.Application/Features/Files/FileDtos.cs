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

public interface IFileService
{
    Task<FileDto> UploadAsync(AttachmentTargetType targetType, long targetId, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<IReadOnlyList<FileDto>> ListAsync(AttachmentTargetType targetType, long targetId, CancellationToken ct = default);
    Task<FileDownload> DownloadAsync(long id, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
