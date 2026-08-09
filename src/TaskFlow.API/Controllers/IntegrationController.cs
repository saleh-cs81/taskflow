using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Integration;
using TaskFlow.Domain.Enums;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/integrations/paymo")]
// Any signed-in user may reach the controller; connect/migrate-all actions are company-admin only
// (per-method [Authorize(Roles=Admins)]), while the catalog + per-project migrate are department-scoped.
[Authorize]
public class IntegrationController(IMigrationService migration, IDepartmentAccess access) : ControllerBase
{
    private const string Admins = "CompanyAdmin,SuperAdmin";

    [HttpGet("status")]
    public async Task<ActionResult<PaymoConnectionDto>> Status(CancellationToken ct)
        => Ok(await migration.GetStatusAsync(ct));

    [HttpPost("connect")]
    [Authorize(Roles = Admins)]
    public async Task<ActionResult<PaymoConnectionDto>> Connect(ConnectPaymoRequest request, CancellationToken ct)
        => Ok(await migration.ConnectAsync(request, ct));

    // Queues the import and returns a job id immediately; poll jobs/{id} for progress.
    [HttpPost("migrate")]
    [Authorize(Roles = Admins)]
    public async Task<ActionResult<StartMigrationResult>> Migrate(CancellationToken ct)
        => Ok(await migration.StartAsync(MigrationJobType.Full, ct));

    [HttpPost("sync")]
    [Authorize(Roles = Admins)]
    public async Task<ActionResult<StartMigrationResult>> Sync(CancellationToken ct)
        => Ok(await migration.StartAsync(MigrationJobType.Incremental, ct));

    // Staged migration: import only the next `count` not-yet-imported projects (count <= 0 => all remaining).
    [HttpPost("migrate-batch")]
    [Authorize(Roles = Admins)]
    public async Task<ActionResult<StartMigrationResult>> MigrateBatch([FromQuery] int count = 10, CancellationToken ct = default)
        => Ok(await migration.StartBatchAsync(count, ct));

    // Step 1 — migrate users only.
    [HttpPost("migrate-users")]
    [Authorize(Roles = Admins)]
    public async Task<ActionResult<StartMigrationResult>> MigrateUsers(CancellationToken ct)
        => Ok(await migration.StartUsersAsync(ct));

    // Step 2 — migrate clients (+ contacts + client financials).
    [HttpPost("migrate-clients")]
    [Authorize(Roles = Admins)]
    public async Task<ActionResult<StartMigrationResult>> MigrateClients(CancellationToken ct)
        => Ok(await migration.StartClientsAsync(ct));

    // Migrate one specific project, fully. Department admins may migrate only projects whose
    // Paymo code matches one of their departments' prefixes.
    [HttpPost("migrate-project/{paymoProjectId:long}")]
    public async Task<ActionResult<StartMigrationResult>> MigrateProject(long paymoProjectId, CancellationToken ct)
    {
        if (!access.IsCompanyAdmin)
        {
            var prefixes = await access.MyPrefixesAsync(ct);
            var catalog = await migration.GetProjectItemsAsync(ct);
            var item = catalog.Items.FirstOrDefault(i => i.PaymoProjectId == paymoProjectId);
            var ok = item is not null && prefixes.Any(pfx => (item.Code ?? "").StartsWith(pfx, StringComparison.OrdinalIgnoreCase));
            if (!ok) throw new ForbiddenAppException("error.forbidden");
        }
        return Ok(await migration.StartProjectAsync(paymoProjectId, ct));
    }

    // Request a graceful stop of the running migration.
    [HttpPost("stop")]
    [Authorize(Roles = Admins)]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        await migration.StopAsync(ct);
        return NoContent();
    }

    // Per-project staging catalog: which projects are imported / pending / failed.
    // Department admins see only the projects whose code matches one of their departments' prefixes.
    [HttpGet("projects")]
    public async Task<ActionResult<MigrationCatalogDto>> Projects(CancellationToken ct)
    {
        var catalog = await migration.GetProjectItemsAsync(ct);
        if (access.IsCompanyAdmin) return Ok(catalog);

        var prefixes = await access.MyPrefixesAsync(ct);
        var items = catalog.Items
            .Where(i => prefixes.Any(pfx => (i.Code ?? "").StartsWith(pfx, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var imported = items.Count(i => i.Status == MigrationProjectStatus.Imported);
        var failed = items.Count(i => i.Status == MigrationProjectStatus.Failed);
        return Ok(new MigrationCatalogDto(items.Count, imported, failed, items.Count - imported - failed, items));
    }

    // Clears imported data so the migration can be re-run from a clean slate.
    [HttpPost("reset")]
    [Authorize(Roles = Admins)]
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
