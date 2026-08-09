using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Org;

public record ClientDto(long Id, string Name, string? CompanyName, string? ContactEmail, string? Phone, string? Notes);
public record ClientRequest(string Name, string? CompanyName, string? ContactEmail, string? Phone, string? Notes);

// Full client profile + roll-up stats for the Client Detail > Overview tab.
public record ClientDetailDto(
    long Id, string Name, string? CompanyName, string? ContactEmail, string? Phone, string? Notes,
    string? Address, string? City, string? Country, string? Website,
    int ProjectCount, int OpenProjectCount, decimal InvoicedTotal, double HoursLogged);

public record ClientContactDto(long Id, long ClientId, string Name, string? Email, string? Phone, string? Position, bool IsMain);
public record ClientContactRequest(string Name, string? Email, string? Phone, string? Position, bool IsMain);

public record ClientProjectDto(long Id, string Name, ProjectStatus Status, DateTime? DueDate, int TaskCount);
public record ClientInvoiceDto(long Id, string Number, InvoiceStatus Status, DateTime IssueDate, DateTime? DueDate, decimal Total, string Currency);
public record ClientTimesheetRowDto(long ProjectId, string ProjectName, double Hours, double BillableHours);

public record DepartmentAdminDto(long UserId, string FullName, string Email);
public record DepartmentDto(
    long Id, string Name, string? CodePrefix, long? ManagerUserId,
    IReadOnlyList<DepartmentAdminDto> Admins, int ProjectCount);
public record DepartmentRequest(string Name, string? CodePrefix, long? ManagerUserId, IReadOnlyList<long>? AdminUserIds);

public record CategoryDto(long Id, string Name, string? Color);
public record CategoryRequest(string Name, string? Color);

public record DiscussionDto(long Id, long ProjectId, string Title, long CreatedById, int PostCount, DateTime CreatedAtUtc);
public record CreateDiscussionRequest(long ProjectId, string Title, string? FirstPost);
public record DiscussionPostDto(long Id, long DiscussionId, long AuthorId, string Body, DateTime CreatedAtUtc);
public record CreatePostRequest(string Body);

public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken ct = default);
    Task<ClientDto> CreateAsync(ClientRequest r, CancellationToken ct = default);
    Task<ClientDto> UpdateAsync(long id, ClientRequest r, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    // Client Detail (Overview/Contacts/Projects/Timesheets/Invoices tabs).
    Task<ClientDetailDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<ClientContactDto>> ListContactsAsync(long id, CancellationToken ct = default);
    Task<ClientContactDto> AddContactAsync(long id, ClientContactRequest r, CancellationToken ct = default);
    Task DeleteContactAsync(long id, long contactId, CancellationToken ct = default);
    Task<IReadOnlyList<ClientProjectDto>> ListProjectsAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<ClientInvoiceDto>> ListInvoicesAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<ClientTimesheetRowDto>> GetTimesheetAsync(long id, CancellationToken ct = default);
}

public interface IDepartmentService
{
    // All departments (company admins). Dept-scoped callers get only the departments they administer.
    Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default);
    Task<DepartmentDto> CreateAsync(DepartmentRequest r, CancellationToken ct = default);
    Task<DepartmentDto> UpdateAsync(long id, DepartmentRequest r, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    // (Re)assign every project to the department whose CodePrefix matches its Code (longest match);
    // projects with no matching prefix go to the catch-all department (null/empty CodePrefix). Idempotent.
    Task<int> AssignProjectsAsync(CancellationToken ct = default);

    // Create the standard department set + prefixes if they don't already exist (idempotent seed).
    Task SeedDefaultsAsync(IReadOnlyList<(string Name, string? Prefix)> defaults, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CategoryRequest r, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(long id, CategoryRequest r, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IDiscussionService
{
    Task<IReadOnlyList<DiscussionDto>> ListAsync(long projectId, CancellationToken ct = default);
    Task<DiscussionDto> CreateAsync(CreateDiscussionRequest r, CancellationToken ct = default);
    Task<IReadOnlyList<DiscussionPostDto>> GetPostsAsync(long discussionId, CancellationToken ct = default);
    Task<DiscussionPostDto> AddPostAsync(long discussionId, CreatePostRequest r, CancellationToken ct = default);
}
