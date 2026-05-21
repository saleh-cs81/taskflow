using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Org;
using TaskFlow.Domain.Entities;

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
}

public class DepartmentService(IAppDbContext db) : IDepartmentService
{
    public async Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default)
        => await db.Departments.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Name, d.ManagerUserId)).ToListAsync(ct);

    public async Task<DepartmentDto> CreateAsync(DepartmentRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationAppException("error.validation");
        var d = new Department { Name = r.Name.Trim(), ManagerUserId = r.ManagerUserId };
        db.Departments.Add(d);
        await db.SaveChangesAsync(ct);
        return new DepartmentDto(d.Id, d.Name, d.ManagerUserId);
    }

    public async Task<DepartmentDto> UpdateAsync(long id, DepartmentRequest r, CancellationToken ct = default)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        d.Name = r.Name.Trim(); d.ManagerUserId = r.ManagerUserId;
        await db.SaveChangesAsync(ct);
        return new DepartmentDto(d.Id, d.Name, d.ManagerUserId);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        db.Departments.Remove(d);
        await db.SaveChangesAsync(ct);
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
            .Select(d => new DiscussionDto(d.Id, d.ProjectId, d.Title, d.CreatedById, d.Posts.Count, d.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<DiscussionDto> CreateAsync(CreateDiscussionRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw new ValidationAppException("error.validation");
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct)) throw new NotFoundAppException("error.not_found");

        var d = new Discussion { ProjectId = r.ProjectId, Title = r.Title.Trim(), CreatedById = currentUser.UserId ?? 0 };
        db.Discussions.Add(d);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(r.FirstPost))
        {
            db.DiscussionPosts.Add(new DiscussionPost { DiscussionId = d.Id, AuthorId = currentUser.UserId ?? 0, Body = r.FirstPost.Trim() });
            await db.SaveChangesAsync(ct);
        }
        return new DiscussionDto(d.Id, d.ProjectId, d.Title, d.CreatedById, string.IsNullOrWhiteSpace(r.FirstPost) ? 0 : 1, d.CreatedAtUtc);
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
