using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

public class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> b)
    {
        b.ToTable("Files");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(400).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.TargetType, x.TargetId });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.LinkUrl).HasMaxLength(400);
        b.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead });
    }
}

public class MentionConfiguration : IEntityTypeConfiguration<Mention>
{
    public void Configure(EntityTypeBuilder<Mention> b)
    {
        b.ToTable("Mentions");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CommentId, x.MentionedUserId }).IsUnique();
        b.HasOne(x => x.Comment).WithMany()
            .HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
    }
}
