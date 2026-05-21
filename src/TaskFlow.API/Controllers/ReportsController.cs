using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.API.Authorization;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Features.Reports;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize]
public class ReportsController(IReportService reports, IReportExporter exporter) : ControllerBase
{
    [HttpGet("dashboard")]
    [RequirePermission(Permissions.Reports.View)]
    public async Task<ActionResult<DashboardStatsDto>> Dashboard(CancellationToken ct)
        => Ok(await reports.GetDashboardAsync(ct));

    [HttpGet("project-progress")]
    [RequirePermission(Permissions.Reports.View)]
    public async Task<ActionResult<IReadOnlyList<ProjectProgressDto>>> ProjectProgress(CancellationToken ct)
        => Ok(await reports.GetProjectProgressAsync(ct));

    [HttpGet("productivity")]
    [RequirePermission(Permissions.Reports.View)]
    public async Task<ActionResult<IReadOnlyList<ProductivityRowDto>>> Productivity(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Ok(await reports.GetProductivityAsync(from, to, ct));

    [HttpGet("calendar")]
    [RequirePermission(Permissions.Reports.View)]
    public async Task<ActionResult<IReadOnlyList<CalendarItemDto>>> Calendar(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] long? projectId = null, CancellationToken ct = default)
        => Ok(await reports.GetCalendarAsync(from, to, projectId, ct));

    // /reports/project-progress/export?format=pdf|xlsx|csv
    [HttpGet("{type}/export")]
    [RequirePermission(Permissions.Reports.Export)]
    public async Task<IActionResult> Export(
        ReportType type, [FromQuery] ExportFormat format = ExportFormat.Csv,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
    {
        var result = await exporter.ExportAsync(type, format, from, to, ct);
        return File(result.Content, result.ContentType, result.FileName);
    }
}
