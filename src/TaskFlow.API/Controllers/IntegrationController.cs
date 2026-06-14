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

    // Queues the import and returns a job id immediately; poll jobs/{id} for progress.
    [HttpPost("migrate")]
    public async Task<ActionResult<StartMigrationResult>> Migrate(CancellationToken ct)
        => Ok(await migration.StartAsync(MigrationJobType.Full, ct));

    [HttpPost("sync")]
    public async Task<ActionResult<StartMigrationResult>> Sync(CancellationToken ct)
        => Ok(await migration.StartAsync(MigrationJobType.Incremental, ct));

    // Staged migration: import only the next `count` not-yet-imported projects (count <= 0 => all remaining).
    [HttpPost("migrate-batch")]
    public async Task<ActionResult<StartMigrationResult>> MigrateBatch([FromQuery] int count = 10, CancellationToken ct = default)
        => Ok(await migration.StartBatchAsync(count, ct));

    // Per-project staging catalog: which projects are imported / pending / failed.
    [HttpGet("projects")]
    public async Task<ActionResult<MigrationCatalogDto>> Projects(CancellationToken ct)
        => Ok(await migration.GetProjectItemsAsync(ct));

    // Clears imported data so the migration can be re-run from a clean slate.
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        await migration.ResetAsync(ct);
        return NoContent();
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<MigrationJobDto>>> Jobs(CancellationToken ct)
        => Ok(await migration.ListJobsAsync(ct));

    [HttpGet("jobs/latest")]
    public async Task<ActionResult<MigrationJobDto?>> Latest(CancellationToken ct)
        => Ok(await migration.GetLatestJobAsync(ct));

    [HttpGet("jobs/{id:long}")]
    public async Task<ActionResult<MigrationJobDto>> Job(long id, CancellationToken ct)
        => Ok(await migration.GetJobAsync(id, ct));

    [HttpGet("jobs/{id:long}/errors")]
    public async Task<ActionResult<IReadOnlyList<MigrationErrorDto>>> Errors(long id, CancellationToken ct)
        => Ok(await migration.ListErrorsAsync(id, ct));
}
