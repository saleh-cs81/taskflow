using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

public class Client : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}

public class Department : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public long? ManagerUserId { get; set; }
}

public class Discussion : TenantEntity
{
    public long ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public long CreatedById { get; set; }

    public ICollection<DiscussionPost> Posts { get; set; } = new List<DiscussionPost>();
}

public class DiscussionPost : TenantEntity
{
    public long DiscussionId { get; set; }
    public long AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;

    public Discussion Discussion { get; set; } = null!;
}
