using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Audit;

namespace TaskFlow.API.Controllers;

// System transaction / audit log. Any signed-in user of the tenant may read it.
[ApiController]
[Route("api/v1/audit")]
[Authorize]
public class AuditController(IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] long? userId = null,
        [FromQuery] string? table = null,
        [FromQuery] int? changeType = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await audit.ListAsync(
            new PageQuery(page, pageSize),
            new AuditFilter(from, to, userId, table, changeType, search),
            ct));

    [HttpGet("tables")]
    public async Task<ActionResult<IReadOnlyList<string>>> Tables(CancellationToken ct = default)
        => Ok(await audit.DistinctTablesAsync(ct));
}
