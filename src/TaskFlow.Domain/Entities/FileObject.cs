using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

// A stored file/attachment. Linked polymorphically to a task/project/comment.
public class FileObject : TenantEntity
{
    public AttachmentTargetType TargetType { get; set; }
    public long TargetId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    // Opaque key in the storage backend (disk path / blob key).
    public string StorageKey { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
}
