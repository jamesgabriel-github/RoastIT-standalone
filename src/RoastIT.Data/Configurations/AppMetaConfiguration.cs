using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Configurations;

public sealed class AppMetaConfiguration : IEntityTypeConfiguration<AppMeta>
{
    public void Configure(EntityTypeBuilder<AppMeta> b)
    {
        b.ToTable("app_meta");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasColumnName("key");
        b.Property(x => x.Value).HasColumnName("value").IsRequired();
        // Import checks app_id to refuse files that aren't Roastline backups (plan §10.5).
        b.HasData(new AppMeta { Key = AppMeta.AppIdKey, Value = AppMeta.AppIdValue });
    }
}
