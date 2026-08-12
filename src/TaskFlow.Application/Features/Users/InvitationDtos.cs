using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Users;

public record InviteRequest(string Email, long RoleId, string? FullName = null);

public record InvitationDto(
    long Id,
    string Email,
    long RoleId,
    string RoleName,
    InvitationStatus Status,
    DateTime ExpiresUtc,
    DateTime CreatedAtUtc,
    // Returned only at creation time so the admin can copy/share the link.
    string? AcceptUrl = null);

public record AcceptInvitationRequest(string Token, string FullName, string Password);

public record AcceptInvitationResult(string Email);

public interface IInvitationService
{
    Task<InvitationDto> CreateAsync(InviteRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<InvitationDto>> ListAsync(CancellationToken ct = default);
    Task RevokeAsync(long id, CancellationToken ct = default);

    // Anonymous + cross-tenant: resolve the invite by token, create the user, assign the role.
    Task<AcceptInvitationResult> AcceptAsync(AcceptInvitationRequest request, CancellationToken ct = default);
}
