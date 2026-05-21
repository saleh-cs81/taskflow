using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

public class Notification : TenantEntity
{
    public long UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

// A user mentioned in a comment (@user). Drives mention notifications.
public class Mention : TenantEntity
{
    public long CommentId { get; set; }
    public long MentionedUserId { get; set; }

    public Comment Comment { get; set; } = null!;
}
