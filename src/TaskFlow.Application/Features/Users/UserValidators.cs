using FluentValidation;

namespace TaskFlow.Application.Features.Users;

public class InviteRequestValidator : AbstractValidator<InviteRequest>
{
    public InviteRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.RoleId).GreaterThan(0);
    }
}

public class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
        => RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
}
