using FluentValidation;

namespace TaskFlow.Application.Features.Tasks;

public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
    }
}

public class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator() => RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
}

public class CreateTaskListRequestValidator : AbstractValidator<CreateTaskListRequest>
{
    public CreateTaskListRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class AddCommentRequestValidator : AbstractValidator<AddCommentRequest>
{
    public AddCommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
}
