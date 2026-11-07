using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Action).HasColumnName("action").IsRequired();
        b.Property(x => x.TargetType).HasColumnName("target_type").IsRequired();
        b.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();
        b.Property(x => x.Details).HasColumnName("details");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
