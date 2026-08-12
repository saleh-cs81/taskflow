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

// Admin sets a new password for another user (no current-password check; signs that user out everywhere).
public record SetPasswordRequest(string NewPassword);

public record RoleDto(long Id, string Name, bool IsSystemRole);

// Permission catalogue + per-role permission editing (company-admin feature).
public record PermissionDto(string Code, string Group);
public record RolePermissionsRequest(IReadOnlyList<string> Permissions);

public interface IUserAdminService
{
    Task<IReadOnlyList<UserListItemDto>> ListAsync(CancellationToken ct = default);
    Task<UserListItemDto> GetAsync(long id, CancellationToken ct = default);
    Task<UserListItemDto> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default);
    Task SetPasswordAsync(long id, string newPassword, CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRolePermissionsAsync(long roleId, CancellationToken ct = default);
    Task SetRolePermissionsAsync(long roleId, RolePermissionsRequest request, CancellationToken ct = default);
}
