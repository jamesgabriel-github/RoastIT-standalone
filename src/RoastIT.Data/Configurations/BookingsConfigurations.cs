using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Configurations;

// Indexes are deliberately not declared yet: they come with Phase 4 hardening (D21).

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_total", "total_amount >= 0");
            t.HasCheckConstraint("ck_orders_discount", "discount_amount >= 0 AND discount_amount <= total_amount");
            t.HasCheckConstraint("ck_orders_paid", "amount_paid >= 0");
        });
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasColumnName("key").ValueGeneratedNever();
        b.Property(x => x.Customer).HasColumnName("customer");
        b.Property(x => x.Phone).HasColumnName("phone");
        b.Property(x => x.OrderDate).HasColumnName("order_date");
        b.Property(x => x.PickupAt).HasColumnName("pickup_at");
        b.Property(x => x.TotalAmount).HasColumnName("total_amount");
        b.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasDefaultValue(0L);
        b.Property(x => x.AmountPaid).HasColumnName("amount_paid").HasDefaultValue(0L);
        b.Property(x => x.Notes).HasColumnName("notes");

        b.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.OrderKey).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.OrderKey).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.HasKey(x => x.BookingId);
        b.Property(x => x.BookingId).HasColumnName("booking_id").ValueGeneratedNever();
        b.Property(x => x.OrderKey).HasColumnName("order_key");
        b.Property(x => x.ProductId).HasColumnName("product_id");
        b.Property(x => x.Status).HasColumnName("status");               // ItemStatus as INTEGER, pipeline order
        b.Property(x => x.StartedCookingAt).HasColumnName("started_cooking_at");

        // Products are deactivated, never deleted, so there is no cascade.
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", t => t.HasCheckConstraint("ck_payments_amount", "amount > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.OrderKey).HasColumnName("order_key");
        b.Property(x => x.Amount).HasColumnName("amount");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
    }
}

public sealed class DailyCounterConfiguration : IEntityTypeConfiguration<DailyCounter>
{
    public void Configure(EntityTypeBuilder<DailyCounter> b)
    {
        b.ToTable("daily_counters");
        b.HasKey(x => x.Date);
        b.Property(x => x.Date).HasColumnName("date").ValueGeneratedNever();
        b.Property(x => x.LastSeq).HasColumnName("last_seq");
    }
}
