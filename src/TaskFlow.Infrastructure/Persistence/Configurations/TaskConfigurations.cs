using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class TaskListConfiguration : IEntityTypeConfiguration<TaskList>
{
    public void Configure(EntityTypeBuilder<TaskList> b)
    {
        b.ToTable("TaskLists");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.ProjectId, x.Position });

        b.HasOne(x => x.Project).WithMany(p => p.TaskLists)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> b)
    {
        b.ToTable("Tasks");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.EstimateHours).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status });
        b.HasIndex(x => new { x.TaskListId, x.Position });

        // Restrict (not Cascade) to avoid multiple cascade paths to Projects
        // (Tasks -> Projects and Tasks -> TaskLists -> Projects). Hard deletes never
        // happen anyway: the SaveChanges interceptor converts deletes to soft deletes.
        b.HasOne(x => x.Project).WithMany()
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TaskList).WithMany(l => l.Tasks)
            .HasForeignKey(x => x.TaskListId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.ParentTask).WithMany(t => t.SubTasks)
            .HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TaskWatcherConfiguration : IEntityTypeConfiguration<TaskWatcher>
{
    public void Configure(EntityTypeBuilder<TaskWatcher> b)
    {
        b.ToTable("TaskWatchers");
        b.HasKey(x => new { x.TaskId, x.UserId });
        b.HasOne(x => x.Task).WithMany(t => t.Watchers)
            .HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> b)
    {
        b.ToTable("TaskDependencies");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.PredecessorTaskId, x.SuccessorTaskId }).IsUnique();

        b.HasOne(x => x.PredecessorTask).WithMany()
            .HasForeignKey(x => x.PredecessorTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SuccessorTask).WithMany()
            .HasForeignKey(x => x.SuccessorTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> b)
    {
        b.ToTable("ChecklistItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.TaskId, x.Position });

        b.HasOne(x => x.Task).WithMany(t => t.ChecklistItems)
            .HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.Property(x => x.Color).HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> b)
    {
        b.ToTable("TaskTags");
        b.HasKey(x => new { x.TaskId, x.TagId });
        b.HasOne(x => x.Task).WithMany(t => t.TaskTags)
            .HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Tag).WithMany(t => t.TaskTags)
            .HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> b)
    {
        b.ToTable("Comments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.TargetType, x.TargetId });
    }
}
