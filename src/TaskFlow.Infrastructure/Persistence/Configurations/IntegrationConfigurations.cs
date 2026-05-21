using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class PaymoConnectionConfiguration : IEntityTypeConfiguration<PaymoConnection>
{
    public void Configure(EntityTypeBuilder<PaymoConnection> b)
    {
        b.ToTable("PaymoConnections");
        b.HasKey(x => x.Id);
        b.Property(x => x.ApiKeyEncrypted).HasMaxLength(1024);
        b.HasIndex(x => x.TenantId).IsUnique();
    }
}

public class MigrationJobConfiguration : IEntityTypeConfiguration<MigrationJob>
{
    public void Configure(EntityTypeBuilder<MigrationJob> b)
    {
        b.ToTable("MigrationJobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
    }
}

public class EntityMappingConfiguration : IEntityTypeConfiguration<EntityMapping>
{
    public void Configure(EntityTypeBuilder<EntityMapping> b)
    {
        b.ToTable("EntityMappings");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
        // The dedup key: one local record per (tenant, entity type, Paymo id).
        b.HasIndex(x => new { x.TenantId, x.EntityType, x.PaymoId }).IsUnique();
    }
}

public class MigrationErrorConfiguration : IEntityTypeConfiguration<MigrationError>
{
    public void Configure(EntityTypeBuilder<MigrationError> b)
    {
        b.ToTable("MigrationErrors");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        b.HasIndex(x => x.MigrationJobId);
    }
}
