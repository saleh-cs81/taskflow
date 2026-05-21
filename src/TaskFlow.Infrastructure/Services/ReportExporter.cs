using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Reports;

namespace TaskFlow.Infrastructure.Services;

public class ReportExporter(IReportService reports, IDateTime clock) : IReportExporter
{
    static ReportExporter() => QuestPDF.Settings.License = LicenseType.Community;

    public async Task<ExportResult> ExportAsync(ReportType type, ExportFormat format, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var to = toUtc ?? clock.UtcNow;
        var from = fromUtc ?? to.AddDays(-30);

        var (title, headers, rows) = type switch
        {
            ReportType.ProjectProgress => await ProjectProgressData(ct),
            ReportType.Productivity => await ProductivityData(from, to, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var name = $"{type}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        return format switch
        {
            ExportFormat.Csv => new ExportResult(BuildCsv(headers, rows), "text/csv", $"{name}.csv"),
            ExportFormat.Xlsx => new ExportResult(BuildXlsx(title, headers, rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}.xlsx"),
            ExportFormat.Pdf => new ExportResult(BuildPdf(title, headers, rows), "application/pdf", $"{name}.pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private async Task<(string, string[], List<string[]>)> ProjectProgressData(CancellationToken ct)
    {
        var data = await reports.GetProjectProgressAsync(ct);
        var headers = new[] { "Project", "Status", "Total", "To Do", "In Progress", "Done", "% Complete" };
        var rows = data.Select(p => new[]
        {
            p.ProjectName, p.Status.ToString(), p.TotalTasks.ToString(),
            p.TodoTasks.ToString(), p.InProgressTasks.ToString(), p.DoneTasks.ToString(),
            p.PercentComplete.ToString(CultureInfo.InvariantCulture) + "%"
        }).ToList();
        return ("Project Progress Report", headers, rows);
    }

    private async Task<(string, string[], List<string[]>)> ProductivityData(DateTime from, DateTime to, CancellationToken ct)
    {
        var data = await reports.GetProductivityAsync(from, to, ct);
        var headers = new[] { "User", "Tasks Completed", "Hours Tracked", "Billable Hours" };
        var rows = data.Select(r => new[]
        {
            r.FullName, r.TasksCompleted.ToString(),
            r.HoursTracked.ToString(CultureInfo.InvariantCulture),
            r.BillableHours.ToString(CultureInfo.InvariantCulture)
        }).ToList();
        return ("Productivity Report", headers, rows);
    }

    private static byte[] BuildCsv(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(Escape)));
        return new UTF8Encoding(true).GetBytes(sb.ToString());

        static string Escape(string v) => v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static byte[] BuildXlsx(string title, string[] headers, List<string[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(title.Length > 31 ? title[..31] : title);

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] BuildPdf(string title, string[] headers, List<string[]> rows)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.Header().Text(title).FontSize(18).Bold().FontColor(Colors.Indigo.Medium);
                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in headers) cols.RelativeColumn();
                    });
                    table.Header(h =>
                    {
                        foreach (var head in headers)
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text(head).Bold();
                    });
                    foreach (var row in rows)
                        foreach (var val in row)
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(val);
                });
                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")).FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }
}
