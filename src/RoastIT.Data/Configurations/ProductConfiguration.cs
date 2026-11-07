using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_price", "price >= 0");
            t.HasCheckConstraint("ck_products_cooking_time", "cooking_time_min >= 1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.Price).HasColumnName("price");                       // long centavos
        b.Property(x => x.CookingTimeMin).HasColumnName("cooking_time_min");
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
