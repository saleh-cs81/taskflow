using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(256);
        b.Property(x => x.DeviceInfo).HasMaxLength(512);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => x.TokenHash);
        b.HasIndex(x => new { x.TenantId, x.UserId });
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("Invitations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Email });
        b.HasOne(x => x.Role).WithMany()
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> b)
    {
        b.ToTable("ActivityLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(150).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.TableName).HasMaxLength(128).IsRequired();
        b.Property(x => x.RecordId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.TableName, x.RecordId });
    }
}
