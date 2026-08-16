using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Users;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class UserAdminService(IAppDbContext db, ICurrentUser currentUser, IPasswordHasher hasher, IDateTime clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(ct);
        var roleMap = await BuildRoleMap(users.Select(u => u.Id).ToList(), ct);
        return users.Select(u => ToDto(u, roleMap)).ToList();
    }

    public async Task<UserListItemDto> GetAsync(long id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        var roleMap = await BuildRoleMap([id], ct);
        return ToDto(user, roleMap);
    }

    public async Task<UserListItemDto> UpdateAsync(long id, UpdateUserRequest r, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");

        // Guard: an admin can't deactivate their own account (lock-out protection).
        if (id == currentUser.UserId && !r.IsActive)
            throw new ValidationAppException("error.validation");

        user.FullName = r.FullName.Trim();
        user.IsActive = r.IsActive;

        // Replace role assignments with the requested set (only valid tenant roles).
        var validRoleIds = await db.Roles.Where(role => r.RoleIds.Contains(role.Id)).Select(role => role.Id).ToListAsync(ct);
        var existing = await db.UserRoles.Where(ur => ur.UserId == id).ToListAsync(ct);
        db.UserRoles.RemoveRange(existing.Where(ur => !validRoleIds.Contains(ur.RoleId)));
        foreach (var rid in validRoleIds.Where(rid => existing.All(ur => ur.RoleId != rid)))
            db.UserRoles.Add(new UserRole { UserId = id, RoleId = rid });

        await db.SaveChangesAsync(ct);
        var roleMap = await BuildRoleMap([id], ct);
        return ToDto(user, roleMap);
    }

    // Admin resets another user's password: hashes it, clears any reset token, revokes all their
    // refresh tokens (signs them out everywhere), and records an audit entry.
    public async Task SetPasswordAsync(long id, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword.Length > 128)
            throw new ValidationAppException("auth.password_length");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");

        user.PasswordHash = hasher.Hash(newPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresUtc = null;

        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedUtc == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedUtc = clock.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = currentUser.UserId,
            TableName = "Auth",
            RecordId = user.Id.ToString(),
            ChangeType = AuditChangeType.PasswordChanged,
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { @event = "admin_reset", target = user.Email }),
            CreatedAtUtc = clock.UtcNow,
            CreatedById = currentUser.UserId
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default)
        => await db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.IsSystemRole)).ToListAsync(ct);

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default)
        => await db.Permissions.AsNoTracking().OrderBy(p => p.Group).ThenBy(p => p.Code)
            .Select(p => new PermissionDto(p.Code, p.Group)).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetRolePermissionsAsync(long roleId, CancellationToken ct = default)
    {
        // Validate the role belongs to the caller's tenant (db.Roles is tenant-filtered) to avoid a cross-tenant read.
        _ = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        return await db.RolePermissions.AsNoTracking().Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission.Code).ToListAsync(ct);
    }

    // Replace a role's permission set. The two admin roles are locked so an admin can't strip their own access.
    public async Task SetRolePermissionsAsync(long roleId, RolePermissionsRequest request, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw new NotFoundAppException("error.not_found");
        if (role.IsSystemRole && (role.Name == "CompanyAdmin" || role.Name == "SuperAdmin"))
            throw new ConflictAppException("error.role_locked");

        var codes = (request.Permissions ?? new List<string>()).Distinct().ToList();
        var permIds = await db.Permissions.Where(p => codes.Contains(p.Code)).Select(p => p.Id).ToListAsync(ct);
        var current = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        db.RolePermissions.RemoveRange(current.Where(rp => !permIds.Contains(rp.PermissionId)));
        foreach (var pid in permIds.Where(pid => current.All(rp => rp.PermissionId != pid)))
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = pid });
        await db.SaveChangesAsync(ct);
    }

    private async Task<ILookup<long, string>> BuildRoleMap(List<long> userIds, CancellationToken ct)
    {
        var rows = await (
            from ur in db.UserRoles
            join role in db.Roles on ur.RoleId equals role.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, role.Name }).ToListAsync(ct);
        return rows.ToLookup(x => x.UserId, x => x.Name);
    }

    private static UserListItemDto ToDto(User u, ILookup<long, string> roleMap) => new(
        u.Id, u.Email, u.FullName, u.Locale, u.IsActive, u.LastLoginUtc, roleMap[u.Id].ToList());
}
