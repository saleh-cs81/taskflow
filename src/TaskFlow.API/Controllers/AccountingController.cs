using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Features.Accounting;
using TaskFlow.Domain.Enums;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/invoices")]
[Authorize]
public class InvoicesController(IInvoiceService svc) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));
    [HttpGet("{id:long}")] public async Task<ActionResult<InvoiceDto>> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(id, ct));
    [HttpPost] public async Task<ActionResult<InvoiceDto>> Create(SaveInvoiceRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));
    [HttpPost("{id:long}/status")] public async Task<ActionResult<InvoiceDto>> SetStatus(long id, [FromQuery] InvoiceStatus status, CancellationToken ct) => Ok(await svc.SetStatusAsync(id, status, ct));
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}

[ApiController]
[Route("api/v1/estimates")]
[Authorize]
public class EstimatesController(IEstimateService svc) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<EstimateDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));
    [HttpGet("{id:long}")] public async Task<ActionResult<EstimateDto>> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(id, ct));
    [HttpPost] public async Task<ActionResult<EstimateDto>> Create(SaveEstimateRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));
    [HttpPost("{id:long}/status")] public async Task<ActionResult<EstimateDto>> SetStatus(long id, [FromQuery] EstimateStatus status, CancellationToken ct) => Ok(await svc.SetStatusAsync(id, status, ct));
    [HttpPost("{id:long}/convert")] public async Task<ActionResult<InvoiceDto>> Convert(long id, CancellationToken ct) => Ok(await svc.ConvertToInvoiceAsync(id, ct));
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}

[ApiController]
[Route("api/v1/expenses")]
[Authorize]
public class ExpensesController(IExpenseService svc) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<ExpenseDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));
    [HttpPost] public async Task<ActionResult<ExpenseDto>> Create(SaveExpenseRequest r, CancellationToken ct) => Ok(await svc.CreateAsync(r, ct));
    [HttpPut("{id:long}")] public async Task<ActionResult<ExpenseDto>> Update(long id, SaveExpenseRequest r, CancellationToken ct) => Ok(await svc.UpdateAsync(id, r, ct));
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}
