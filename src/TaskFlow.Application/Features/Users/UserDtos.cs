namespace TaskFlow.Application.Features.Users;

public record UserListItemDto(
    long Id,
    string Email,
    string FullName,
    string Locale,
    bool IsActive,
    DateTime? LastLoginUtc,
    IReadOnlyList<string> Roles);

public record UpdateUserRequest(string FullName, bool IsActive, IReadOnlyList<long> RoleIds);

public record RoleDto(long Id, string Name, bool IsSystemRole);

public interface IUserAdminService
{
    Task<IReadOnlyList<UserListItemDto>> ListAsync(CancellationToken ct = default);
    Task<UserListItemDto> GetAsync(long id, CancellationToken ct = default);
    Task<UserListItemDto> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default);
}
