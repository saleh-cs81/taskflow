using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Recurring;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/recurring-tasks")]
[Authorize]
public class RecurringTasksController(IRecurringTaskService service, IDateTime clock) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Tasks.View)]
    public async Task<ActionResult<IReadOnlyList<RecurringRuleDto>>> List(CancellationToken ct)
        => Ok(await service.ListAsync(ct));

    [HttpPost]
    [RequirePermission(Permissions.Tasks.Create)]
    public async Task<ActionResult<RecurringRuleDto>> Create(CreateRecurringRuleRequest request, CancellationToken ct)
        => Ok(await service.CreateAsync(request, ct));

    [HttpPost("{id:long}/toggle")]
    [RequirePermission(Permissions.Tasks.Update)]
    public async Task<IActionResult> Toggle(long id, CancellationToken ct)
    {
        await service.ToggleAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Tasks.Delete)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    // Manual trigger (useful for testing / on-demand generation).
    [HttpPost("run-now")]
    [RequirePermission(Permissions.Tasks.Create)]
    public async Task<ActionResult<object>> RunNow(CancellationToken ct)
        => Ok(new { generated = await service.GenerateDueAsync(clock.UtcNow, ct) });
}
