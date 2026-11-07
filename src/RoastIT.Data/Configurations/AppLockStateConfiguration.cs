using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Configurations;

public sealed class AppLockStateConfiguration : IEntityTypeConfiguration<AppLockState>
{
    public void Configure(EntityTypeBuilder<AppLockState> b)
    {
        b.ToTable("app_lock", t => t.HasCheckConstraint("ck_app_lock_single_row", "id = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PinHash).HasColumnName("pin_hash").IsRequired();
        b.Property(x => x.RecoveryCodeHash).HasColumnName("recovery_code_hash").IsRequired();
        b.Property(x => x.FailedAttempts).HasColumnName("failed_attempts");
        b.Property(x => x.LockedUntil).HasColumnName("locked_until");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}
