using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Application.Common.Interfaces;

// Abstraction over the EF Core DbContext so the Application layer stays persistence-agnostic.
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<ActivityLog> ActivityLogs { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<ProjectCategory> ProjectCategories { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<Milestone> Milestones { get; }
    DbSet<TaskList> TaskLists { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<TaskWatcher> TaskWatchers { get; }
    DbSet<TaskDependency> TaskDependencies { get; }
    DbSet<ChecklistItem> ChecklistItems { get; }
    DbSet<Tag> Tags { get; }
    DbSet<TaskTag> TaskTags { get; }
    DbSet<Comment> Comments { get; }

    DbSet<TimeEntry> TimeEntries { get; }
    DbSet<Timesheet> Timesheets { get; }

    DbSet<FileObject> Files { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Mention> Mentions { get; }

    DbSet<PaymoConnection> PaymoConnections { get; }
    DbSet<MigrationJob> MigrationJobs { get; }
    DbSet<EntityMapping> EntityMappings { get; }
    DbSet<MigrationError> MigrationErrors { get; }

    DbSet<RecurringTaskRule> RecurringTaskRules { get; }

    DbSet<Plan> Plans { get; }
    DbSet<Subscription> Subscriptions { get; }

    DbSet<Client> Clients { get; }
    DbSet<Department> Departments { get; }
    DbSet<Discussion> Discussions { get; }
    DbSet<DiscussionPost> DiscussionPosts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
