using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Auth;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;
using TaskFlow.Infrastructure.Persistence.Seeding;

namespace TaskFlow.Infrastructure.Services;

public class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    IJwtService jwt,
    ITenantContext tenant,
    IDateTime clock,
    ICurrentUser currentUser) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, string? ip, CancellationToken ct = default)
    {
        var normalizedEmail = req.Email.Trim().ToUpperInvariant();

        // Email is treated as globally unique for sign-in simplicity (Phase 1).
        var exists = await db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (exists) throw new ConflictAppException("auth.email_taken");

        var newTenant = new Tenant
        {
            Name = req.CompanyName.Trim(),
            Slug = await GenerateUniqueSlugAsync(req.CompanyName, ct),
            DefaultLocale = req.Locale,
            SubscriptionStatus = SubscriptionStatus.Trial,
            TrialEndsUtc = clock.UtcNow.AddDays(14)
        };
        db.Tenants.Add(newTenant);
        await db.SaveChangesAsync(ct);

        // Stamp tenant for subsequent inserts (roles/user).
        tenant.SetTenant(newTenant.Id);

        var roles = await RoleSeeder.CreateTenantRolesAsync(db, newTenant.Id, ct);
        var adminRole = roles[SystemRole.CompanyAdmin];

        var user = new User
        {
            TenantId = newTenant.Id,
            Email = req.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            FullName = req.FullName.Trim(),
            PasswordHash = hasher.Hash(req.Password),
            Locale = req.Locale,
            IsActive = true,
            EmailConfirmed = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
        await db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ip, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, string? ip, CancellationToken ct = default)
    {
        var normalizedEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user is null || !hasher.Verify(req.Password, user.PasswordHash))
        {
            if (user is not null) await AuthEventAsync(AuditChangeType.LoginFailed, user, "invalid_credentials", ct);
            throw new UnauthorizedAppException("auth.invalid_credentials");
        }
        if (!user.IsActive)
        {
            await AuthEventAsync(AuditChangeType.LoginFailed, user, "account_disabled", ct);
            throw new UnauthorizedAppException("auth.account_disabled");
        }

        tenant.SetTenant(user.TenantId);
        user.LastLoginUtc = clock.UtcNow;
        db.AuditLogs.Add(AuthLog(AuditChangeType.Login, user, null));
        await db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ip, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest req, string? ip, CancellationToken ct = default)
    {
        var hash = jwt.HashRefreshToken(req.RefreshToken);
        var token = await db.RefreshTokens.IgnoreQueryFilters()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || !token.IsActive)
            throw new UnauthorizedAppException("auth.invalid_refresh_token");

        tenant.SetTenant(token.TenantId);

        // Rotate: revoke the old token, issue a new pair.
        token.RevokedUtc = clock.UtcNow;
        var response = await IssueTokensAsync(token.User, ip, ct, replacedTokenHashSetter: newHash => token.ReplacedByTokenHash = newHash);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = jwt.HashRefreshToken(refreshToken);
        var token = await db.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is { RevokedUtc: null })
        {
            token.RevokedUtc = clock.UtcNow;
            tenant.SetTenant(token.TenantId);
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = token.TenantId, UserId = token.UserId, TableName = "Auth",
                RecordId = token.UserId.ToString(), ChangeType = AuditChangeType.Logout,
                CreatedAtUtc = clock.UtcNow, CreatedById = token.UserId
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct = default)
    {
        var normalizedEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        // Always succeed silently to avoid email enumeration.
        if (user is null) return;

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        user.PasswordResetTokenHash = jwt.HashRefreshToken(rawToken);
        user.PasswordResetExpiresUtc = clock.UtcNow.AddHours(1);
        await db.SaveChangesAsync(ct);

        // TODO(Phase 4): send email with rawToken via IEmailSender.
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct = default)
    {
        var normalizedEmail = req.Email.Trim().ToUpperInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        var tokenHash = jwt.HashRefreshToken(req.Token);
        if (user is null
            || user.PasswordResetTokenHash != tokenHash
            || user.PasswordResetExpiresUtc is null
            || user.PasswordResetExpiresUtc < clock.UtcNow)
        {
            throw new ValidationAppException("auth.invalid_reset_token");
        }

        user.PasswordHash = hasher.Hash(req.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresUtc = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest req, CancellationToken ct = default)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == uid, ct)
            ?? throw new UnauthorizedAppException("error.unauthorized");

        if (!hasher.Verify(req.CurrentPassword, user.PasswordHash))
            throw new ValidationAppException("auth.invalid_credentials");

        user.PasswordHash = hasher.Hash(req.NewPassword);

        // Revoke all active refresh tokens so other sessions are signed out.
        var tokens = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == uid && t.RevokedUtc == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedUtc = clock.UtcNow;

        db.AuditLogs.Add(AuthLog(AuditChangeType.PasswordChanged, user, null));
        await db.SaveChangesAsync(ct);
    }

    // ---- Auth audit helpers (written into the shared AuditLog table, TableName="Auth") ----
    private AuditLog AuthLog(AuditChangeType type, User user, string? reason)
        => new()
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            TableName = "Auth",
            RecordId = user.Id.ToString(),
            ChangeType = type,
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(
                reason is null ? new { email = user.Email } : (object)new { email = user.Email, reason }),
            CreatedAtUtc = clock.UtcNow,
            CreatedById = user.Id
        };

    // Failed logins happen before a tenant is set on the request; stamp + save on their own.
    private async Task AuthEventAsync(AuditChangeType type, User user, string reason, CancellationToken ct)
    {
        tenant.SetTenant(user.TenantId);
        db.AuditLogs.Add(AuthLog(type, user, reason));
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(
        User user, string? ip, CancellationToken ct, Action<string>? replacedTokenHashSetter = null)
    {
        var roleNames = await (
            from ur in db.UserRoles.IgnoreQueryFilters()
            join r in db.Roles.IgnoreQueryFilters() on ur.RoleId equals r.Id
            where ur.UserId == user.Id
            select r.Name).ToListAsync(ct);

        var permissions = await (
            from ur in db.UserRoles.IgnoreQueryFilters()
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == user.Id
            select p.Code).Distinct().ToListAsync(ct);

        var pair = jwt.CreateTokens(user, roleNames, permissions);
        var refreshHash = jwt.HashRefreshToken(pair.RefreshToken);
        replacedTokenHashSetter?.Invoke(refreshHash);

        db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresUtc = pair.RefreshTokenExpiresUtc,
            CreatedByIp = ip
        });
        await db.SaveChangesAsync(ct);

        var dto = new UserDto(user.Id, user.TenantId, user.Email, user.FullName, user.Locale, roleNames, permissions);
        return new AuthResponse(pair.AccessToken, pair.AccessTokenExpiresUtc, pair.RefreshToken, pair.RefreshTokenExpiresUtc, dto);
    }

    private async Task<string> GenerateUniqueSlugAsync(string companyName, CancellationToken ct)
    {
        var baseSlug = new string(companyName.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "tenant";
        if (baseSlug.Length > 60) baseSlug = baseSlug[..60];

        var slug = baseSlug;
        var i = 1;
        while (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, ct))
            slug = $"{baseSlug}-{++i}";
        return slug;
    }
}
