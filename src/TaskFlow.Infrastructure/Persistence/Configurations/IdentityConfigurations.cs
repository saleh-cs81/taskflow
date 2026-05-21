using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.DefaultLocale).HasMaxLength(5).HasDefaultValue("en");
        b.Property(x => x.TimeZone).HasMaxLength(64).HasDefaultValue("UTC");
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Locale).HasMaxLength(5).HasDefaultValue("en");
        b.Property(x => x.TimeZone).HasMaxLength(64).HasDefaultValue("UTC");
        b.Property(x => x.RowVersion).IsRowVersion();

        // Email is unique per tenant.
        b.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();

        b.HasOne(x => x.Tenant).WithMany(t => t.Users)
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.Group).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasOne(x => x.User).WithMany(u => u.UserRoles)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Role).WithMany(r => r.UserRoles)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });
        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions)
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Permission).WithMany(p => p.RolePermissions)
            .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}
