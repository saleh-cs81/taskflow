using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Persistence.Seeding;

public static class RoleSeeder
{
    // Default permission set per system role.
    private static readonly Dictionary<SystemRole, string[]> RolePermissionMap = new()
    {
        [SystemRole.CompanyAdmin] = Permissions.All.Select(p => p.Code).ToArray(),
        [SystemRole.ProjectManager] =
        [
            Permissions.Projects.View, Permissions.Projects.Create, Permissions.Projects.Update,
            Permissions.Tasks.View, Permissions.Tasks.Create, Permissions.Tasks.Update, Permissions.Tasks.Delete, Permissions.Tasks.Assign,
            Permissions.TimeTracking.View, Permissions.TimeTracking.Track, Permissions.TimeTracking.Approve,
            Permissions.Users.View, Permissions.Users.Invite,
            Permissions.Reports.View, Permissions.Reports.Export
        ],
        [SystemRole.TeamLeader] =
        [
            Permissions.Projects.View,
            Permissions.Tasks.View, Permissions.Tasks.Create, Permissions.Tasks.Update, Permissions.Tasks.Assign,
            Permissions.TimeTracking.View, Permissions.TimeTracking.Track,
            Permissions.Users.View, Permissions.Reports.View
        ],
        [SystemRole.Employee] =
        [
            Permissions.Projects.View, Permissions.Tasks.View, Permissions.Tasks.Update,
            Permissions.TimeTracking.View, Permissions.TimeTracking.Track
        ],
        [SystemRole.Client] = [Permissions.Projects.View, Permissions.Tasks.View],
    };

    // Creates the standard set of tenant roles + their permission assignments.
    public static async Task<Dictionary<SystemRole, Role>> CreateTenantRolesAsync(
        IAppDbContext db, long tenantId, CancellationToken ct)
    {
        var permissionIdByCode = await db.Permissions
            .ToDictionaryAsync(p => p.Code, p => p.Id, ct);

        var result = new Dictionary<SystemRole, Role>();

        foreach (var (systemRole, codes) in RolePermissionMap)
        {
            var role = new Role
            {
                TenantId = tenantId,
                Name = systemRole.ToString(),
                NormalizedName = systemRole.ToString().ToUpperInvariant(),
                IsSystemRole = true,
                Description = $"System role: {systemRole}"
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);

            foreach (var code in codes)
            {
                if (permissionIdByCode.TryGetValue(code, out var pid))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = pid });
            }
            result[systemRole] = role;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }
}
