using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

public class Invoice : TenantEntity
{
    public string Number { get; set; } = string.Empty;     // e.g. INV-0001
    public long? ClientId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }        // percent, e.g. 16
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public Client? Client { get; set; }
    public ICollection<InvoiceLineItem> Items { get; set; } = new List<InvoiceLineItem>();
}

public class InvoiceLineItem : TenantEntity
{
    public long InvoiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public Invoice Invoice { get; set; } = null!;
}

public class Estimate : TenantEntity
{
    public string Number { get; set; } = string.Empty;     // e.g. EST-0001
    public long? ClientId { get; set; }
    public EstimateStatus Status { get; set; } = EstimateStatus.Draft;
    public DateTime IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public Client? Client { get; set; }
    public ICollection<EstimateLineItem> Items { get; set; } = new List<EstimateLineItem>();
}

public class EstimateLineItem : TenantEntity
{
    public long EstimateId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public Estimate Estimate { get; set; } = null!;
}

public class InvoicePayment : TenantEntity
{
    public long InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }

    public Invoice Invoice { get; set; } = null!;
}

public class Expense : TenantEntity
{
    public long? ProjectId { get; set; }
    public long? ClientId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime Date { get; set; }
    public bool IsBillable { get; set; }

    public Project? Project { get; set; }
    public Client? Client { get; set; }
}
