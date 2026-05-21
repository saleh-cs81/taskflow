using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Features.Integration;
using TaskFlow.Domain.Enums;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/integrations/paymo")]
// Only company admins (or super admins) can connect/migrate.
[Authorize(Roles = "CompanyAdmin,SuperAdmin")]
public class IntegrationController(IMigrationService migration) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<PaymoConnectionDto>> Status(CancellationToken ct)
        => Ok(await migration.GetStatusAsync(ct));

    [HttpPost("connect")]
    public async Task<ActionResult<PaymoConnectionDto>> Connect(ConnectPaymoRequest request, CancellationToken ct)
        => Ok(await migration.ConnectAsync(request, ct));

    [HttpPost("migrate")]
    public async Task<ActionResult<MigrationJobDto>> Migrate(CancellationToken ct)
        => Ok(await migration.RunAsync(MigrationJobType.Full, ct));

    [HttpPost("sync")]
    public async Task<ActionResult<MigrationJobDto>> Sync(CancellationToken ct)
        => Ok(await migration.RunAsync(MigrationJobType.Incremental, ct));

    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<MigrationJobDto>>> Jobs(CancellationToken ct)
        => Ok(await migration.ListJobsAsync(ct));

    [HttpGet("jobs/{id:long}")]
    public async Task<ActionResult<MigrationJobDto>> Job(long id, CancellationToken ct)
        => Ok(await migration.GetJobAsync(id, ct));

    [HttpGet("jobs/{id:long}/errors")]
    public async Task<ActionResult<IReadOnlyList<MigrationErrorDto>>> Errors(long id, CancellationToken ct)
        => Ok(await migration.ListErrorsAsync(id, ct));
}
