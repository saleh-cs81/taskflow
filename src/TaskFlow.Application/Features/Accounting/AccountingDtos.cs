using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Accounting;

public record LineItemDto(string Description, decimal Quantity, decimal UnitPrice);

// ---- Invoices ----
public record InvoiceDto(
    long Id, string Number, long? ClientId, string? ClientName, InvoiceStatus Status,
    DateTime IssueDate, DateTime? DueDate, string Currency, string? Notes,
    decimal Subtotal, decimal TaxRate, decimal TaxAmount, decimal Total,
    IReadOnlyList<LineItemDto> Items);

public record SaveInvoiceRequest(
    long? ClientId, DateTime issueDate, DateTime? dueDate, string Currency,
    string? Notes, decimal TaxRate, IReadOnlyList<LineItemDto> Items);

public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> ListAsync(CancellationToken ct = default);
    Task<InvoiceDto> GetAsync(long id, CancellationToken ct = default);
    Task<InvoiceDto> CreateAsync(SaveInvoiceRequest r, CancellationToken ct = default);
    Task<InvoiceDto> SetStatusAsync(long id, InvoiceStatus status, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

// ---- Estimates ----
public record EstimateDto(
    long Id, string Number, long? ClientId, string? ClientName, EstimateStatus Status,
    DateTime IssueDate, DateTime? ExpiryDate, string Currency, string? Notes,
    decimal Subtotal, decimal TaxRate, decimal TaxAmount, decimal Total,
    IReadOnlyList<LineItemDto> Items);

public record SaveEstimateRequest(
    long? ClientId, DateTime IssueDate, DateTime? ExpiryDate, string Currency,
    string? Notes, decimal TaxRate, IReadOnlyList<LineItemDto> Items);

public interface IEstimateService
{
    Task<IReadOnlyList<EstimateDto>> ListAsync(CancellationToken ct = default);
    Task<EstimateDto> GetAsync(long id, CancellationToken ct = default);
    Task<EstimateDto> CreateAsync(SaveEstimateRequest r, CancellationToken ct = default);
    Task<EstimateDto> SetStatusAsync(long id, EstimateStatus status, CancellationToken ct = default);
    Task<InvoiceDto> ConvertToInvoiceAsync(long id, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

// ---- Expenses ----
public record ExpenseDto(
    long Id, long? ProjectId, long? ClientId, string Category, string? Description,
    decimal Amount, string Currency, DateTime Date, bool IsBillable);

public record SaveExpenseRequest(
    long? ProjectId, long? ClientId, string Category, string? Description,
    decimal Amount, string Currency, DateTime Date, bool IsBillable);

public interface IExpenseService
{
    Task<IReadOnlyList<ExpenseDto>> ListAsync(CancellationToken ct = default);
    Task<ExpenseDto> CreateAsync(SaveExpenseRequest r, CancellationToken ct = default);
    Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest r, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
