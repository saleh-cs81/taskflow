namespace TaskFlow.Application.Features.Org;

public record ClientDto(long Id, string Name, string? CompanyName, string? ContactEmail, string? Phone, string? Notes);
public record ClientRequest(string Name, string? CompanyName, string? ContactEmail, string? Phone, string? Notes);

public record DepartmentDto(long Id, string Name, long? ManagerUserId);
public record DepartmentRequest(string Name, long? ManagerUserId);

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
}

public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default);
    Task<DepartmentDto> CreateAsync(DepartmentRequest r, CancellationToken ct = default);
    Task<DepartmentDto> UpdateAsync(long id, DepartmentRequest r, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
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
