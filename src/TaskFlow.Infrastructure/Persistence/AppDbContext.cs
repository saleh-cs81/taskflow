using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<ProjectCategory> ProjectCategories => Set<ProjectCategory>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<TaskList> TaskLists => Set<TaskList>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskWatcher> TaskWatchers => Set<TaskWatcher>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();
    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<Timesheet> Timesheets => Set<Timesheet>();

    public DbSet<FileObject> Files => Set<FileObject>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Mention> Mentions => Set<Mention>();

    public DbSet<PaymoConnection> PaymoConnections => Set<PaymoConnection>();
    public DbSet<MigrationJob> MigrationJobs => Set<MigrationJob>();
    public DbSet<EntityMapping> EntityMappings => Set<EntityMapping>();
    public DbSet<MigrationError> MigrationErrors => Set<MigrationError>();

    public DbSet<RecurringTaskRule> RecurringTaskRules => Set<RecurringTaskRule>();

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Discussion> Discussions => Set<Discussion>();
    public DbSet<DiscussionPost> DiscussionPosts => Set<DiscussionPost>();

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<Estimate> Estimates => Set<Estimate>();
    public DbSet<EstimateLineItem> EstimateLineItems => Set<EstimateLineItem>();
    public DbSet<Expense> Expenses => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Multi-tenancy + soft delete global query filters.
        // SuperAdmin (no tenant) sees all rows; otherwise rows are scoped to the current tenant.
        modelBuilder.Entity<User>().HasQueryFilter(e =>
            !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Role>().HasQueryFilter(e =>
            !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(e =>
            !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Invitation>().HasQueryFilter(e =>
            !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<ActivityLog>().HasQueryFilter(e =>
            !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e =>
            !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));

        modelBuilder.Entity<Tenant>().HasQueryFilter(e => !e.IsDeleted);

        // Phase 2: project & task entities.
        modelBuilder.Entity<ProjectCategory>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Project>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<ProjectMember>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Milestone>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<TaskList>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<TaskItem>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<TaskDependency>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<ChecklistItem>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Tag>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Comment>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));

        // Phase 3: time tracking.
        modelBuilder.Entity<TimeEntry>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Timesheet>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));

        // Phase 4: collaboration.
        modelBuilder.Entity<FileObject>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Notification>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<Mention>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));

        // Paymo integration.
        modelBuilder.Entity<PaymoConnection>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<MigrationJob>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<EntityMapping>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        modelBuilder.Entity<MigrationError>().HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));

        modelBuilder.Entity<RecurringTaskRule>(b =>
        {
            b.ToTable("RecurringTaskRules");
            b.Property(x => x.TitleTemplate).HasMaxLength(300).IsRequired();
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });

        // Billing: Plans are global; Subscriptions are tenant-scoped.
        modelBuilder.Entity<Plan>(b =>
        {
            b.ToTable("Plans");
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(80).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.PriceMonthly).HasPrecision(18, 2);
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<Subscription>(b =>
        {
            b.ToTable("Subscriptions");
            b.Property(x => x.Provider).HasMaxLength(30);
            b.Property(x => x.ProviderSubscriptionId).HasMaxLength(100);
            b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });

        // Org & collaboration gap-fill.
        modelBuilder.Entity<Client>(b =>
        {
            b.ToTable("Clients");
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.CompanyName).HasMaxLength(150);
            b.Property(x => x.ContactEmail).HasMaxLength(256);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.City).HasMaxLength(100);
            b.Property(x => x.Country).HasMaxLength(100);
            b.Property(x => x.Website).HasMaxLength(200);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<ClientContact>(b =>
        {
            b.ToTable("ClientContacts");
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.Position).HasMaxLength(100);
            b.HasOne(x => x.Client).WithMany(c => c.Contacts)
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<TaskAssignee>(b =>
        {
            b.ToTable("TaskAssignees");
            b.HasKey(x => new { x.TaskId, x.UserId });
            b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Department>(b =>
        {
            b.ToTable("Departments");
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<Discussion>(b =>
        {
            b.ToTable("Discussions");
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<DiscussionPost>(b =>
        {
            b.ToTable("DiscussionPosts");
            b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            b.HasOne(x => x.Discussion).WithMany(d => d.Posts)
                .HasForeignKey(x => x.DiscussionId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });

        // Accounting: invoices, estimates, expenses.
        modelBuilder.Entity<Invoice>(b =>
        {
            b.ToTable("Invoices");
            b.Property(x => x.Number).HasMaxLength(40).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Notes).HasMaxLength(2000);
            foreach (var p in new[] { "Subtotal", "TaxRate", "TaxAmount", "Total" })
                b.Property(p).HasPrecision(18, 2);
            b.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.Number });
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<InvoiceLineItem>(b =>
        {
            b.ToTable("InvoiceLineItems");
            b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            foreach (var p in new[] { "Quantity", "UnitPrice", "LineTotal" })
                b.Property(p).HasPrecision(18, 2);
            b.HasOne(x => x.Invoice).WithMany(i => i.Items).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<InvoicePayment>(b =>
        {
            b.ToTable("InvoicePayments");
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<Estimate>(b =>
        {
            b.ToTable("Estimates");
            b.Property(x => x.Number).HasMaxLength(40).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Notes).HasMaxLength(2000);
            foreach (var p in new[] { "Subtotal", "TaxRate", "TaxAmount", "Total" })
                b.Property(p).HasPrecision(18, 2);
            b.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.Number });
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<EstimateLineItem>(b =>
        {
            b.ToTable("EstimateLineItems");
            b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            foreach (var p in new[] { "Quantity", "UnitPrice", "LineTotal" })
                b.Property(p).HasPrecision(18, 2);
            b.HasOne(x => x.Estimate).WithMany(i => i.Items).HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
        modelBuilder.Entity<Expense>(b =>
        {
            b.ToTable("Expenses");
            b.Property(x => x.Category).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.SetNull);
            b.HasQueryFilter(e => !e.IsDeleted && (tenant.IsSuperAdmin || e.TenantId == tenant.TenantId));
        });
    }
}
