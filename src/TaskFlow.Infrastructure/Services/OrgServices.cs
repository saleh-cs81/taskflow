using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Org;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class ClientService(IAppDbContext db) : IClientService
{
    public async Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken ct = default)
        => await db.Clients.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new ClientDto(c.Id, c.Name, c.CompanyName, c.ContactEmail, c.Phone, c.Notes)).ToListAsync(ct);

    public async Task<ClientDto> CreateAsync(ClientRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationAppException("error.validation");
        var c = new Client { Name = r.Name.Trim(), CompanyName = r.CompanyName, ContactEmail = r.ContactEmail, Phone = r.Phone, Notes = r.Notes };
        db.Clients.Add(c);
        await db.SaveChangesAsync(ct);
        return new ClientDto(c.Id, c.Name, c.CompanyName, c.ContactEmail, c.Phone, c.Notes);
    }

    public async Task<ClientDto> UpdateAsync(long id, ClientRequest r, CancellationToken ct = default)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        c.Name = r.Name.Trim(); c.CompanyName = r.CompanyName; c.ContactEmail = r.ContactEmail; c.Phone = r.Phone; c.Notes = r.Notes;
        await db.SaveChangesAsync(ct);
        return new ClientDto(c.Id, c.Name, c.CompanyName, c.ContactEmail, c.Phone, c.Notes);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        db.Clients.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ClientDetailDto> GetAsync(long id, CancellationToken ct = default)
    {
        var c = await db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");

        var projectIds = await db.Projects.AsNoTracking().Where(p => p.ClientId == id).Select(p => p.Id).ToListAsync(ct);
        var openCount = await db.Projects.AsNoTracking()
            .CountAsync(p => p.ClientId == id
                && p.Status != ProjectStatus.Completed && p.Status != ProjectStatus.Archived && p.Status != ProjectStatus.Cancelled, ct);
        var invoiced = await db.Invoices.AsNoTracking().Where(i => i.ClientId == id).SumAsync(i => (decimal?)i.Total, ct) ?? 0m;
        var seconds = projectIds.Count == 0 ? 0
            : await db.TimeEntries.AsNoTracking().Where(t => projectIds.Contains(t.ProjectId)).SumAsync(t => (long?)t.DurationSeconds, ct) ?? 0;

        return new ClientDetailDto(c.Id, c.Name, c.CompanyName, c.ContactEmail, c.Phone, c.Notes,
            c.Address, c.City, c.Country, c.Website,
            projectIds.Count, openCount, invoiced, Math.Round(seconds / 3600.0, 1));
    }

    public async Task<IReadOnlyList<ClientContactDto>> ListContactsAsync(long id, CancellationToken ct = default)
        => await db.ClientContacts.AsNoTracking().Where(x => x.ClientId == id)
            .OrderByDescending(x => x.IsMain).ThenBy(x => x.Name)
            .Select(x => new ClientContactDto(x.Id, x.ClientId, x.Name, x.Email, x.Phone, x.Position, x.IsMain)).ToListAsync(ct);

    public async Task<ClientContactDto> AddContactAsync(long id, ClientContactRequest r, CancellationToken ct = default)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == id, ct)) throw new NotFoundAppException("error.not_found");
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationAppException("error.validation");
        var x = new ClientContact { ClientId = id, Name = r.Name.Trim(), Email = r.Email, Phone = r.Phone, Position = r.Position, IsMain = r.IsMain };
        db.ClientContacts.Add(x);
        await db.SaveChangesAsync(ct);
        return new ClientContactDto(x.Id, x.ClientId, x.Name, x.Email, x.Phone, x.Position, x.IsMain);
    }

    public async Task DeleteContactAsync(long id, long contactId, CancellationToken ct = default)
    {
        var x = await db.ClientContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.ClientId == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        db.ClientContacts.Remove(x);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ClientProjectDto>> ListProjectsAsync(long id, CancellationToken ct = default)
        => await db.Projects.AsNoTracking().Where(p => p.ClientId == id).OrderBy(p => p.Name)
            .Select(p => new ClientProjectDto(p.Id, p.Name, p.Status, p.DueDate, db.Tasks.Count(t => t.ProjectId == p.Id)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClientInvoiceDto>> ListInvoicesAsync(long id, CancellationToken ct = default)
        => await db.Invoices.AsNoTracking().Where(i => i.ClientId == id)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id)
            .Select(i => new ClientInvoiceDto(i.Id, i.Number, i.Status, i.IssueDate, i.DueDate, i.Total, i.Currency)).ToListAsync(ct);

    public async Task<IReadOnlyList<ClientTimesheetRowDto>> GetTimesheetAsync(long id, CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking().Where(p => p.ClientId == id).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        if (projects.Count == 0) return [];
        var ids = projects.Select(p => p.Id).ToList();
        var rows = await db.TimeEntries.AsNoTracking().Where(t => ids.Contains(t.ProjectId))
            .GroupBy(t => t.ProjectId)
            .Select(g => new { ProjectId = g.Key, Seconds = g.Sum(t => (long)t.DurationSeconds), Billable = g.Where(t => t.IsBillable).Sum(t => (long)t.DurationSeconds) })
            .ToListAsync(ct);
        return projects
            .Select(p => { var r = rows.FirstOrDefault(x => x.ProjectId == p.Id);
                return new ClientTimesheetRowDto(p.Id, p.Name, Math.Round((r?.Seconds ?? 0) / 3600.0, 1), Math.Round((r?.Billable ?? 0) / 3600.0, 1)); })
            .Where(r => r.Hours > 0).OrderByDescending(r => r.Hours).ToList();
    }
}

public class DepartmentService(IAppDbContext db, IDepartmentAccess access) : IDepartmentService
{
    public async Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default)
    {
        var q = db.Departments.AsNoTracking().OrderBy(d => d.Name).AsQueryable();
        if (!access.IsCompanyAdmin)
        {
            var mine = await access.MyDepartmentIdsAsync(ct);
            q = q.Where(d => mine.Contains(d.Id));
        }
        return await ToDtosAsync(await q.ToListAsync(ct), ct);
    }

    private async Task<IReadOnlyList<DepartmentDto>> ToDtosAsync(List<Department> depts, CancellationToken ct)
    {
        var ids = depts.Select(d => d.Id).ToList();
        var admins = await (from da in db.DepartmentAdmins.AsNoTracking()
                            join u in db.Users.AsNoTracking() on da.UserId equals u.Id
                            where ids.Contains(da.DepartmentId)
                            select new { da.DepartmentId, da.UserId, u.FullName, u.Email }).ToListAsync(ct);
        var counts = await db.Projects.AsNoTracking()
            .Where(p => p.DepartmentId != null && ids.Contains(p.DepartmentId!.Value))
            .GroupBy(p => p.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() }).ToListAsync(ct);
        return depts.Select(d => new DepartmentDto(
            d.Id, d.Name, d.CodePrefix, d.ManagerUserId,
            admins.Where(a => a.DepartmentId == d.Id)
                  .Select(a => new DepartmentAdminDto(a.UserId, a.FullName, a.Email)).ToList(),
            counts.FirstOrDefault(c => c.DepartmentId == d.Id)?.Count ?? 0)).ToList();
    }

    public async Task<DepartmentDto> CreateAsync(DepartmentRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationAppException("error.validation");
        var d = new Department { Name = r.Name.Trim(), CodePrefix = Norm(r.CodePrefix), ManagerUserId = r.ManagerUserId };
        db.Departments.Add(d);
        await db.SaveChangesAsync(ct);
        await SetAdminsAsync(d.Id, r.AdminUserIds, ct);
        return (await ToDtosAsync(new List<Department> { d }, ct))[0];
    }

    public async Task<DepartmentDto> UpdateAsync(long id, DepartmentRequest r, CancellationToken ct = default)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationAppException("error.validation");
        d.Name = r.Name.Trim(); d.CodePrefix = Norm(r.CodePrefix); d.ManagerUserId = r.ManagerUserId;
        await SetAdminsAsync(d.Id, r.AdminUserIds, ct);
        return (await ToDtosAsync(new List<Department> { d }, ct))[0];
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        foreach (var p in await db.Projects.Where(p => p.DepartmentId == id).ToListAsync(ct)) p.DepartmentId = null;
        db.DepartmentAdmins.RemoveRange(await db.DepartmentAdmins.Where(a => a.DepartmentId == id).ToListAsync(ct));
        db.Departments.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    private async Task SetAdminsAsync(long deptId, IReadOnlyList<long>? userIds, CancellationToken ct)
    {
        var existing = await db.DepartmentAdmins.Where(a => a.DepartmentId == deptId).ToListAsync(ct);
        var want = (userIds ?? new List<long>()).Distinct().ToHashSet();
        foreach (var a in existing.Where(a => !want.Contains(a.UserId))) db.DepartmentAdmins.Remove(a);
        var have = existing.Select(a => a.UserId).ToHashSet();
        foreach (var uid in want.Where(u => !have.Contains(u)))
            db.DepartmentAdmins.Add(new DepartmentAdmin { DepartmentId = deptId, UserId = uid });
        await db.SaveChangesAsync(ct);
    }

    // Longest-prefix match on Project.Code; no code / no match -> catch-all department (empty prefix).
    public async Task<int> AssignProjectsAsync(CancellationToken ct = default)
    {
        var depts = await db.Departments.AsNoTracking().ToListAsync(ct);
        var catchAll = depts.Where(d => string.IsNullOrEmpty(d.CodePrefix)).OrderBy(d => d.Name).FirstOrDefault();
        var prefixed = depts.Where(d => !string.IsNullOrEmpty(d.CodePrefix))
            .OrderByDescending(d => d.CodePrefix!.Length).ToList();
        var changed = 0;
        foreach (var p in await db.Projects.ToListAsync(ct))
        {
            long? target;
            if (!string.IsNullOrEmpty(p.Code))
            {
                var match = prefixed.FirstOrDefault(d => p.Code!.StartsWith(d.CodePrefix!, StringComparison.OrdinalIgnoreCase));
                target = match?.Id ?? catchAll?.Id;
            }
            else target = catchAll?.Id;
            if (p.DepartmentId != target) { p.DepartmentId = target; changed++; }
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task SeedDefaultsAsync(IReadOnlyList<(string Name, string? Prefix)> defaults, CancellationToken ct = default)
    {
        var have = (await db.Departments.Select(d => d.Name).ToListAsync(ct))
            .Select(n => n.ToLowerInvariant()).ToHashSet();
        var added = false;
        foreach (var (name, prefix) in defaults)
        {
            if (have.Contains(name.ToLowerInvariant())) continue;
            db.Departments.Add(new Department { Name = name, CodePrefix = Norm(prefix) });
            added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    private static string? Norm(string? prefix) => string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();
}

public class DepartmentAccess(IAppDbContext db, ICurrentUser currentUser, ITenantContext tenant) : IDepartmentAccess
{
    public bool IsCompanyAdmin =>
        tenant.IsSuperAdmin || currentUser.Roles.Any(r => r is "CompanyAdmin" or "SuperAdmin");

    public async Task<IReadOnlyList<long>> MyDepartmentIdsAsync(CancellationToken ct = default)
    {
        var uid = currentUser.UserId;
        if (uid is null) return [];
        return await db.DepartmentAdmins.AsNoTracking()
            .Where(a => a.UserId == uid).Select(a => a.DepartmentId).Distinct().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> MyPrefixesAsync(CancellationToken ct = default)
    {
        var ids = await MyDepartmentIdsAsync(ct);
        if (ids.Count == 0) return [];
        return await db.Departments.AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.CodePrefix != null && d.CodePrefix != "")
            .Select(d => d.CodePrefix!).ToListAsync(ct);
    }

    public async Task<bool> CanManageProjectAsync(long projectId, CancellationToken ct = default)
    {
        if (IsCompanyAdmin || currentUser.Permissions.Contains("projects.update")) return true;
        var deptId = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId).Select(p => p.DepartmentId).FirstOrDefaultAsync(ct);
        if (deptId is null) return false;
        return (await MyDepartmentIdsAsync(ct)).Contains(deptId.Value);
    }
}

public class CategoryService(IAppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct = default)
        => await db.ProjectCategories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color)).ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(CategoryRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationAppException("error.validation");
        var c = new ProjectCategory { Name = r.Name.Trim(), Color = r.Color };
        db.ProjectCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.Color);
    }

    public async Task<CategoryDto> UpdateAsync(long id, CategoryRequest r, CancellationToken ct = default)
    {
        var c = await db.ProjectCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        c.Name = r.Name.Trim(); c.Color = r.Color;
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.Color);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var c = await db.ProjectCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        db.ProjectCategories.Remove(c);
        await db.SaveChangesAsync(ct);
    }
}

public class DiscussionService(IAppDbContext db, ICurrentUser currentUser) : IDiscussionService
{
    public async Task<IReadOnlyList<DiscussionDto>> ListAsync(long projectId, CancellationToken ct = default)
        => await db.Discussions.AsNoTracking().Where(d => d.ProjectId == projectId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new DiscussionDto(d.Id, d.ProjectId, d.Title, d.CreatedById ?? 0, d.Posts.Count, d.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<DiscussionDto> CreateAsync(CreateDiscussionRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw new ValidationAppException("error.validation");
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct)) throw new NotFoundAppException("error.not_found");

        var d = new Discussion { ProjectId = r.ProjectId, Title = r.Title.Trim() };
        db.Discussions.Add(d); // CreatedById stamped by the audit interceptor
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(r.FirstPost))
        {
            db.DiscussionPosts.Add(new DiscussionPost { DiscussionId = d.Id, AuthorId = currentUser.UserId ?? 0, Body = r.FirstPost.Trim() });
            await db.SaveChangesAsync(ct);
        }
        return new DiscussionDto(d.Id, d.ProjectId, d.Title, d.CreatedById ?? 0, string.IsNullOrWhiteSpace(r.FirstPost) ? 0 : 1, d.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<DiscussionPostDto>> GetPostsAsync(long discussionId, CancellationToken ct = default)
        => await db.DiscussionPosts.AsNoTracking().Where(p => p.DiscussionId == discussionId)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new DiscussionPostDto(p.Id, p.DiscussionId, p.AuthorId, p.Body, p.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<DiscussionPostDto> AddPostAsync(long discussionId, CreatePostRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Body)) throw new ValidationAppException("error.validation");
        if (!await db.Discussions.AnyAsync(d => d.Id == discussionId, ct)) throw new NotFoundAppException("error.not_found");

        var post = new DiscussionPost { DiscussionId = discussionId, AuthorId = currentUser.UserId ?? 0, Body = r.Body.Trim() };
        db.DiscussionPosts.Add(post);
        await db.SaveChangesAsync(ct);
        return new DiscussionPostDto(post.Id, discussionId, post.AuthorId, post.Body, post.CreatedAtUtc);
    }
}
