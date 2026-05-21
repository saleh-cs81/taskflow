using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.Users;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/invitations")]
public class InvitationsController(
    IInvitationService invitations,
    IValidator<InviteRequest> inviteValidator,
    IValidator<AcceptInvitationRequest> acceptValidator) : ControllerBase
{
    [Authorize]
    [HttpGet]
    [RequirePermission(Permissions.Users.Invite)]
    public async Task<ActionResult<IReadOnlyList<InvitationDto>>> List(CancellationToken ct)
        => Ok(await invitations.ListAsync(ct));

    [Authorize]
    [HttpPost]
    [RequirePermission(Permissions.Users.Invite)]
    public async Task<ActionResult<InvitationDto>> Create(InviteRequest request, CancellationToken ct)
    {
        await inviteValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await invitations.CreateAsync(request, ct));
    }

    [Authorize]
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Users.Invite)]
    public async Task<IActionResult> Revoke(long id, CancellationToken ct)
    {
        await invitations.RevokeAsync(id, ct);
        return NoContent();
    }

    // Anonymous: a new teammate accepts their invite and sets a password.
    [AllowAnonymous]
    [HttpPost("accept")]
    public async Task<ActionResult<AcceptInvitationResult>> Accept(AcceptInvitationRequest request, CancellationToken ct)
    {
        await acceptValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await invitations.AcceptAsync(request, ct));
    }
}
