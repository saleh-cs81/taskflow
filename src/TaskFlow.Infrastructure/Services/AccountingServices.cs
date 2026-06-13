using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Accounting;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

internal static class MoneyMath
{
    public static (decimal subtotal, decimal taxAmount, decimal total) Totals(
        IEnumerable<LineItemDto> items, decimal taxRate)
    {
        var subtotal = items.Sum(i => Math.Round(i.Quantity * i.UnitPrice, 2));
        var tax = Math.Round(subtotal * taxRate / 100m, 2);
        return (subtotal, tax, subtotal + tax);
    }
}

public class InvoiceService(IAppDbContext db, IDateTime clock) : IInvoiceService
{
    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(CancellationToken ct = default)
    {
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Client).Include(i => i.Items)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Id).ToListAsync(ct);
        return invoices.Select(ToDto).ToList();
    }

    public async Task<InvoiceDto> GetAsync(long id, CancellationToken ct = default)
    {
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Client).Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        return ToDto(inv);
    }

    public async Task<InvoiceDto> CreateAsync(SaveInvoiceRequest r, CancellationToken ct = default)
    {
        var (subtotal, tax, total) = MoneyMath.Totals(r.Items, r.TaxRate);
        var inv = new Invoice
        {
            Number = await NextNumberAsync(ct),
            ClientId = r.ClientId,
            Status = InvoiceStatus.Draft,
            IssueDate = r.issueDate == default ? clock.UtcNow.Date : r.issueDate,
            DueDate = r.dueDate,
            Currency = string.IsNullOrWhiteSpace(r.Currency) ? "USD" : r.Currency,
            Notes = r.Notes,
            TaxRate = r.TaxRate, Subtotal = subtotal, TaxAmount = tax, Total = total,
            Items = r.Items.Select(i => new InvoiceLineItem
            {
                Description = i.Description, Quantity = i.Quantity, UnitPrice = i.UnitPrice,
                LineTotal = Math.Round(i.Quantity * i.UnitPrice, 2)
            }).ToList()
        };
        db.Invoices.Add(inv);
        await db.SaveChangesAsync(ct);
        return await GetAsync(inv.Id, ct);
    }

    public async Task<InvoiceDto> SetStatusAsync(long id, InvoiceStatus status, CancellationToken ct = default)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        inv.Status = status;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        db.Invoices.Remove(inv);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var count = await db.Invoices.IgnoreQueryFilters().CountAsync(ct); // per-tenant via interceptor stamping
        return $"INV-{(count + 1):D4}";
    }

    internal static InvoiceDto ToDto(Invoice i) => new(
        i.Id, i.Number, i.ClientId, i.Client?.Name, i.Status, i.IssueDate, i.DueDate, i.Currency, i.Notes,
        i.Subtotal, i.TaxRate, i.TaxAmount, i.Total,
        i.Items.Select(x => new LineItemDto(x.Description, x.Quantity, x.UnitPrice)).ToList());
}

public class EstimateService(IAppDbContext db, IInvoiceService invoices, IDateTime clock) : IEstimateService
{
    public async Task<IReadOnlyList<EstimateDto>> ListAsync(CancellationToken ct = default)
    {
        var list = await db.Estimates.AsNoTracking().Include(e => e.Client).Include(e => e.Items)
            .OrderByDescending(e => e.IssueDate).ThenByDescending(e => e.Id).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<EstimateDto> GetAsync(long id, CancellationToken ct = default)
    {
        var e = await db.Estimates.AsNoTracking().Include(x => x.Client).Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        return ToDto(e);
    }

    public async Task<EstimateDto> CreateAsync(SaveEstimateRequest r, CancellationToken ct = default)
    {
        var (subtotal, tax, total) = MoneyMath.Totals(r.Items, r.TaxRate);
        var est = new Estimate
        {
            Number = await NextNumberAsync(ct),
            ClientId = r.ClientId, Status = EstimateStatus.Draft,
            IssueDate = r.IssueDate == default ? clock.UtcNow.Date : r.IssueDate,
            ExpiryDate = r.ExpiryDate,
            Currency = string.IsNullOrWhiteSpace(r.Currency) ? "USD" : r.Currency,
            Notes = r.Notes, TaxRate = r.TaxRate, Subtotal = subtotal, TaxAmount = tax, Total = total,
            Items = r.Items.Select(i => new EstimateLineItem
            {
                Description = i.Description, Quantity = i.Quantity, UnitPrice = i.UnitPrice,
                LineTotal = Math.Round(i.Quantity * i.UnitPrice, 2)
            }).ToList()
        };
        db.Estimates.Add(est);
        await db.SaveChangesAsync(ct);
        return await GetAsync(est.Id, ct);
    }

    public async Task<EstimateDto> SetStatusAsync(long id, EstimateStatus status, CancellationToken ct = default)
    {
        var e = await db.Estimates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        e.Status = status;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> ConvertToInvoiceAsync(long id, CancellationToken ct = default)
    {
        var e = await db.Estimates.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        var req = new SaveInvoiceRequest(e.ClientId, clock.UtcNow.Date, null, e.Currency, e.Notes, e.TaxRate,
            e.Items.Select(i => new LineItemDto(i.Description, i.Quantity, i.UnitPrice)).ToList());
        var invoice = await invoices.CreateAsync(req, ct);
        await SetStatusAsync(id, EstimateStatus.Accepted, ct);
        return invoice;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var e = await db.Estimates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        db.Estimates.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
        => $"EST-{(await db.Estimates.CountAsync(ct) + 1):D4}";

    private static EstimateDto ToDto(Estimate e) => new(
        e.Id, e.Number, e.ClientId, e.Client?.Name, e.Status, e.IssueDate, e.ExpiryDate, e.Currency, e.Notes,
        e.Subtotal, e.TaxRate, e.TaxAmount, e.Total,
        e.Items.Select(x => new LineItemDto(x.Description, x.Quantity, x.UnitPrice)).ToList());
}

public class ExpenseService(IAppDbContext db, IDateTime clock) : IExpenseService
{
    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(CancellationToken ct = default)
        => await db.Expenses.AsNoTracking().OrderByDescending(e => e.Date)
            .Select(e => new ExpenseDto(e.Id, e.ProjectId, e.ClientId, e.Category, e.Description,
                e.Amount, e.Currency, e.Date, e.IsBillable)).ToListAsync(ct);

    public async Task<ExpenseDto> CreateAsync(SaveExpenseRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Category)) throw new ValidationAppException("error.validation");
        var e = new Expense
        {
            ProjectId = r.ProjectId, ClientId = r.ClientId, Category = r.Category.Trim(),
            Description = r.Description, Amount = r.Amount,
            Currency = string.IsNullOrWhiteSpace(r.Currency) ? "USD" : r.Currency,
            Date = r.Date == default ? clock.UtcNow.Date : r.Date, IsBillable = r.IsBillable
        };
        db.Expenses.Add(e);
        await db.SaveChangesAsync(ct);
        return new ExpenseDto(e.Id, e.ProjectId, e.ClientId, e.Category, e.Description, e.Amount, e.Currency, e.Date, e.IsBillable);
    }

    public async Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest r, CancellationToken ct = default)
    {
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        e.ProjectId = r.ProjectId; e.ClientId = r.ClientId; e.Category = r.Category.Trim();
        e.Description = r.Description; e.Amount = r.Amount; e.Currency = r.Currency; e.Date = r.Date; e.IsBillable = r.IsBillable;
        await db.SaveChangesAsync(ct);
        return new ExpenseDto(e.Id, e.ProjectId, e.ClientId, e.Category, e.Description, e.Amount, e.Currency, e.Date, e.IsBillable);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundAppException("error.not_found");
        db.Expenses.Remove(e);
        await db.SaveChangesAsync(ct);
    }
}
