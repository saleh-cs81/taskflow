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
    }
}
