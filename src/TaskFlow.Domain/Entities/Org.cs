using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

public class Client : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    // Optional address fields (populated by Paymo import).
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Website { get; set; }

    public ICollection<ClientContact> Contacts { get; set; } = new List<ClientContact>();
}

public class ClientContact : TenantEntity
{
    public long ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Position { get; set; }
    public bool IsMain { get; set; }

    public Client Client { get; set; } = null!;
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
    // Creator is captured by the inherited audit field BaseEntity.CreatedById.

    public ICollection<DiscussionPost> Posts { get; set; } = new List<DiscussionPost>();
}

public class DiscussionPost : TenantEntity
{
    public long DiscussionId { get; set; }
    public long AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;

    public Discussion Discussion { get; set; } = null!;
}
