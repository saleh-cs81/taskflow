using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class ProjectCategoryConfiguration : IEntityTypeConfiguration<ProjectCategory>
{
    public void Configure(EntityTypeBuilder<ProjectCategory> b)
    {
        b.ToTable("ProjectCategories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Color).HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Name });
    }
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("Projects");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Color).HasMaxLength(20);
        b.Property(x => x.BudgetAmount).HasPrecision(18, 2);
        b.Property(x => x.BudgetHours).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.Status });

        b.HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> b)
    {
        b.ToTable("ProjectMembers");
        b.HasKey(x => x.Id);
        b.Property(x => x.RoleInProject).HasMaxLength(50);
        b.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();

        b.HasOne(x => x.Project).WithMany(p => p.Members)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> b)
    {
        b.ToTable("Milestones");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Project).WithMany(p => p.Milestones)
            .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
