using FluentValidation;

namespace TaskFlow.Application.Features.TimeTracking;

public class StartTimerRequestValidator : AbstractValidator<StartTimerRequest>
{
    public StartTimerRequestValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}

public class ManualTimeEntryRequestValidator : AbstractValidator<ManualTimeEntryRequest>
{
    public ManualTimeEntryRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc).WithMessage("End time must be after start time.");
    }
}

public class CreateTimesheetRequestValidator : AbstractValidator<CreateTimesheetRequest>
{
    public CreateTimesheetRequestValidator()
        => RuleFor(x => x.PeriodEnd).GreaterThan(x => x.PeriodStart).WithMessage("Period end must be after period start.");
}
