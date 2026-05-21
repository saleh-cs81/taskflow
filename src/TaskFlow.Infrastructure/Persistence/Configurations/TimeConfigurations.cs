using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> b)
    {
        b.ToTable("TimeEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.UserId, x.StartUtc });
        // At most one running timer per user (filtered unique index).
        b.HasIndex(x => x.UserId)
            .HasFilter("[IsRunning] = 1 AND [IsDeleted] = 0")
            .IsUnique()
            .HasDatabaseName("UX_TimeEntries_OneRunningPerUser");

        b.HasOne(x => x.Project).WithMany()
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Task).WithMany()
            .HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TimesheetConfiguration : IEntityTypeConfiguration<Timesheet>
{
    public void Configure(EntityTypeBuilder<Timesheet> b)
    {
        b.ToTable("Timesheets");
        b.HasKey(x => x.Id);
        b.Property(x => x.ReviewNote).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.UserId, x.PeriodStart });

        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
