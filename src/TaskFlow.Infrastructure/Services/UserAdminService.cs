using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Users;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Services;

public class UserAdminService(IAppDbContext db, ICurrentUser currentUser) : IUserAdminService
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

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default)
        => await db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.IsSystemRole)).ToListAsync(ct);

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
