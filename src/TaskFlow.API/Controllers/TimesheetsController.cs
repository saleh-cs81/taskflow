using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.TimeTracking;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/timesheets")]
[Authorize]
public class TimesheetsController(
    ITimeTrackingService time,
    IValidator<CreateTimesheetRequest> createValidator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.TimeTracking.View)]
    public async Task<ActionResult<IReadOnlyList<TimesheetDto>>> List(CancellationToken ct)
        => Ok(await time.ListTimesheetsAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<ActionResult<TimesheetDto>> Create(CreateTimesheetRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await time.CreateTimesheetAsync(request, ct));
    }

    [HttpPost("{id:long}/submit")]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<ActionResult<TimesheetDto>> Submit(long id, CancellationToken ct)
        => Ok(await time.SubmitTimesheetAsync(id, ct));

    [HttpPost("{id:long}/approve")]
    [RequirePermission(Permissions.TimeTracking.Approve)]
    public async Task<ActionResult<TimesheetDto>> Approve(long id, ReviewTimesheetRequest request, CancellationToken ct)
        => Ok(await time.ApproveTimesheetAsync(id, request, ct));

    [HttpPost("{id:long}/reject")]
    [RequirePermission(Permissions.TimeTracking.Approve)]
    public async Task<ActionResult<TimesheetDto>> Reject(long id, ReviewTimesheetRequest request, CancellationToken ct)
        => Ok(await time.RejectTimesheetAsync(id, request, ct));
}
