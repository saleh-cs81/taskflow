using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.API.Common;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.TimeTracking;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/time")]
[Authorize]
public class TimeController(
    ITimeTrackingService time,
    IValidator<StartTimerRequest> startValidator,
    IValidator<ManualTimeEntryRequest> manualValidator) : ControllerBase
{
    [HttpPost("start")]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<ActionResult<TimeEntryDto>> Start(StartTimerRequest request, CancellationToken ct)
    {
        await startValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await time.StartTimerAsync(request, ct));
    }

    [HttpPost("stop")]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<ActionResult<TimeEntryDto>> Stop(CancellationToken ct)
        => Ok(await time.StopTimerAsync(ct));

    [HttpGet("running")]
    [RequirePermission(Permissions.TimeTracking.View)]
    public async Task<ActionResult<TimeEntryDto?>> Running(CancellationToken ct)
        => Ok(await time.GetRunningAsync(ct));

    [HttpPost("entries")]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<ActionResult<TimeEntryDto>> AddManual(ManualTimeEntryRequest request, CancellationToken ct)
    {
        await manualValidator.ValidateAndThrowAppAsync(request, ct);
        return Ok(await time.AddManualAsync(request, ct));
    }

    [HttpPut("entries/{id:long}")]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<ActionResult<TimeEntryDto>> Update(long id, UpdateTimeEntryRequest request, CancellationToken ct)
        => Ok(await time.UpdateAsync(id, request, ct));

    [HttpDelete("entries/{id:long}")]
    [RequirePermission(Permissions.TimeTracking.Track)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await time.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("entries")]
    [RequirePermission(Permissions.TimeTracking.View)]
    public async Task<ActionResult<IReadOnlyList<TimeEntryDto>>> List(
        [FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] long? projectId = null, [FromQuery] long? userId = null, CancellationToken ct = default)
        => Ok(await time.ListAsync(from, to, projectId, userId, ct));

    [HttpGet("summary")]
    [RequirePermission(Permissions.TimeTracking.View)]
    public async Task<ActionResult<TimeSummaryDto>> Summary(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] long? userId = null, CancellationToken ct = default)
        => Ok(await time.SummaryAsync(from, to, userId, ct));
}
