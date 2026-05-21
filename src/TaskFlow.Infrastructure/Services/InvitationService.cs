using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Users;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class InvitationService(
    IAppDbContext db,
    ITenantContext tenant,
    IJwtService jwt,
    IPasswordHasher hasher,
    IDateTime clock,
    IEmailSender email) : IInvitationService
{
    public async Task<InvitationDto> CreateAsync(InviteRequest r, CancellationToken ct = default)
    {
        var normalizedEmail = r.Email.Trim().ToUpperInvariant();

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            throw new ConflictAppException("error.conflict");

        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == r.RoleId, ct)
            ?? throw new NotFoundAppException("error.not_found");

        // Supersede any pending invite for the same email.
        var pending = await db.Invitations
            .Where(i => i.Email == r.Email.Trim() && i.Status == InvitationStatus.Pending).ToListAsync(ct);
        foreach (var p in pending) p.Status = InvitationStatus.Revoked;

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var invitation = new Invitation
        {
            Email = r.Email.Trim(),
            RoleId = r.RoleId,
            TokenHash = jwt.HashRefreshToken(rawToken),
            ExpiresUtc = clock.UtcNow.AddDays(7),
            Status = InvitationStatus.Pending
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var acceptUrl = $"/accept-invite.html?token={rawToken}";
        await email.SendAsync(r.Email.Trim(), "You're invited to TaskFlow",
            $"You have been invited. Accept here: {acceptUrl}", ct);

        return new InvitationDto(invitation.Id, invitation.Email, role.Id, role.Name,
            invitation.Status, invitation.ExpiresUtc, invitation.CreatedAtUtc, acceptUrl);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(CancellationToken ct = default)
        => await (
            from i in db.Invitations
            join role in db.Roles on i.RoleId equals role.Id
            where i.Status == InvitationStatus.Pending
            orderby i.CreatedAtUtc descending
            select new InvitationDto(i.Id, i.Email, role.Id, role.Name, i.Status, i.ExpiresUtc, i.CreatedAtUtc, null))
            .ToListAsync(ct);

    public async Task RevokeAsync(long id, CancellationToken ct = default)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        inv.Status = InvitationStatus.Revoked;
        await db.SaveChangesAsync(ct);
    }

    public async Task<AcceptInvitationResult> AcceptAsync(AcceptInvitationRequest r, CancellationToken ct = default)
    {
        var tokenHash = jwt.HashRefreshToken(r.Token);

        // No tenant context yet (anonymous): bypass the tenant query filter.
        var inv = await db.Invitations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

        if (inv is null || inv.Status != InvitationStatus.Pending)
            throw new ValidationAppException("auth.invalid_invitation");
        if (inv.ExpiresUtc < clock.UtcNow)
        {
            inv.Status = InvitationStatus.Expired;
            await db.SaveChangesAsync(ct);
            throw new ValidationAppException("auth.invalid_invitation");
        }

        var normalizedEmail = inv.Email.ToUpperInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            throw new ConflictAppException("error.conflict");

        // Stamp the invite's tenant so inserts land in the right tenant.
        tenant.SetTenant(inv.TenantId);

        var user = new User
        {
            TenantId = inv.TenantId,
            Email = inv.Email,
            NormalizedEmail = normalizedEmail,
            FullName = r.FullName.Trim(),
            PasswordHash = hasher.Hash(r.Password),
            IsActive = true,
            EmailConfirmed = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = inv.RoleId });
        inv.Status = InvitationStatus.Accepted;
        inv.AcceptedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new AcceptInvitationResult(user.Email);
    }
}
