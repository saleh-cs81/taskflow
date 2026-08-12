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
    // Projects whose Code starts with this prefix belong to this department (e.g. "ASP-").
    // Null/empty means the catch-all bucket for projects with no code / no matching prefix.
    public string? CodePrefix { get; set; }

    public ICollection<DepartmentAdmin> Admins { get; set; } = new List<DepartmentAdmin>();
    public ICollection<DepartmentMember> Members { get; set; } = new List<DepartmentMember>();
}

// A user who administers a department (a user may admin several departments).
public class DepartmentAdmin : TenantEntity
{
    public long DepartmentId { get; set; }
    public long UserId { get; set; }

    public Department Department { get; set; } = null!;
}

// A user who is a team member of a department (a user may belong to several departments).
public class DepartmentMember : TenantEntity
{
    public long DepartmentId { get; set; }
    public long UserId { get; set; }

    public Department Department { get; set; } = null!;
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
