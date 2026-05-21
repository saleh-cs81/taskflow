using FluentValidation;

namespace TaskFlow.Application.Features.Projects;

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Code).MaximumLength(30);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate.HasValue && x.DueDate.HasValue)
            .WithMessage("Due date must be on or after the start date.");
    }
}

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Code).MaximumLength(30);
    }
}

public class CreateMilestoneRequestValidator : AbstractValidator<CreateMilestoneRequest>
{
    public CreateMilestoneRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
}
